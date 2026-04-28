using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpaBookingSystem.Api.Dtos.Payments;
using SpaBookingSystem.Api.Options;
using SpaBookingSystem.Api.Services.Email;
using SpaBookingSystem.Api.Services.Momo;
using SpaBookingSystem.Api.Services;
using SpaBookingSystem.ApplicationCore.Constants;
using SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.DataLayer;

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private static readonly string[] AllowedMethods = [PaymentMethodNames.Momo, PaymentMethodNames.BankTransfer];
    private static readonly string[] AdminAllowedStatuses =
    [
        PaymentStatusNames.Pending,
        PaymentStatusNames.Paid,
        PaymentStatusNames.Rejected,
        PaymentStatusNames.Refunded
    ];

    private static readonly TimeSpan PaymentTtl = TimeSpan.FromMinutes(5);
    private const int MaxPaymentAttempts = 3;

    private readonly SpaDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IMomoService _momoService;
    private readonly MomoOptions _momoOptions;
    private readonly BankTransferOptions _bankTransferOptions;
    private readonly IBookingStaffingService _bookingStaffingService;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        SpaDbContext db,
        IEmailSender emailSender,
        IMomoService momoService,
        IOptions<MomoOptions> momoOptions,
        IOptions<BankTransferOptions> bankTransferOptions,
        IBookingStaffingService bookingStaffingService,
        ILogger<PaymentsController> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _momoService = momoService;
        _momoOptions = momoOptions.Value;
        _bankTransferOptions = bankTransferOptions.Value;
        _bookingStaffingService = bookingStaffingService;
        _logger = logger;
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Cashier}")]
    [HttpGet]
    public async Task<ActionResult<List<PaymentDto>>> GetAll()
    {
        var data = await _db.Payments
            .AsNoTracking()
            .Include(x => x.Booking)
            .OrderByDescending(x => x.PaidAt)
            .ToListAsync();

        return Ok(data.Select(x => MapPayment(x)));
    }

    [Authorize]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PaymentDto>> GetById(int id)
    {
        var entity = await _db.Payments
            .AsNoTracking()
            .Include(x => x.Booking)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (entity == null)
            return NotFound(new { message = "Payment not found" });

        if (!CanAccessPayment(entity.Booking))
            return Forbid();

        return Ok(MapPayment(entity));
    }

    [Authorize]
    [HttpGet("booking/{bookingId:int}/latest")]
    public async Task<ActionResult<PaymentDto>> GetLatestByBooking(int bookingId)
    {
        var booking = await _db.Bookings
            .AsNoTracking()
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == bookingId);

        if (booking == null)
            return NotFound(new { message = "Booking not found" });

        if (!CanAccessBooking(booking))
            return Forbid();

        var latestPayment = booking.Payments
            .OrderByDescending(x => x.PaidAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();

        if (latestPayment == null)
            return NotFound(new { message = "No payment request found for this booking" });

        await _db.Entry(latestPayment).Reference(x => x.Booking).LoadAsync();
        return Ok(MapPayment(latestPayment));
    }

    [Authorize(Roles = RoleNames.Customer)]
    [HttpPost]
    public async Task<ActionResult<PaymentDto>> Create(PaymentCreateDto dto, CancellationToken cancellationToken)
    {
        var method = NormalizeMethod(dto.Method);
        if (!AllowedMethods.Contains(method))
            return BadRequest(new { message = "Invalid payment method" });

        var currentEmail = GetCurrentEmail();
        if (string.IsNullOrWhiteSpace(currentEmail))
            return Unauthorized(new { message = "Invalid token" });

        var booking = await _db.Bookings
            .Include(x => x.Payments)
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.Service)
            .FirstOrDefaultAsync(x => x.Id == dto.BookingId, cancellationToken);

        if (booking == null)
            return NotFound(new { message = "Booking not found" });

        if (!string.Equals(booking.Email.Trim(), currentEmail, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        if (booking.PaymentStatus == PaymentStatusNames.Paid)
            return BadRequest(new { message = "This booking has already been paid." });

        var availabilityError = ValidateBookingPaymentWindow(booking);
        if (availabilityError is not null)
            return BadRequest(new { message = availabilityError });

        var latestActivePayment = booking.Payments
            .OrderByDescending(p => p.PaidAt)
            .ThenByDescending(p => p.Id)
            .FirstOrDefault(p => IsActivePaymentStatus(p.Status));

        if (latestActivePayment != null)
        {
            if (latestActivePayment.Method == PaymentMethodNames.Momo && method == PaymentMethodNames.Momo)
                return await ReuseOrRefreshMomoPaymentAsync(booking, latestActivePayment, cancellationToken);

            if (latestActivePayment.Method == PaymentMethodNames.BankTransfer && method == PaymentMethodNames.BankTransfer)
                return Ok(MapPayment(latestActivePayment));

            return BadRequest(new { message = "This booking already has an active payment request." });
        }

        if (method == PaymentMethodNames.Momo && booking.PaymentAttempts >= MaxPaymentAttempts)
            return BadRequest(new { message = "Payment retry limit reached for this booking." });

        var paymentCode = $"PAY-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        var payment = new Payment
        {
            BookingId = booking.Id,
            PaymentCode = paymentCode,
            Method = method,
            Amount = method == PaymentMethodNames.Momo
                ? Convert.ToInt64(decimal.Round(booking.TotalAmount, 0, MidpointRounding.AwayFromZero))
                : booking.TotalAmount,
            Status = method == PaymentMethodNames.Momo
                ? PaymentStatusNames.Pending
                : PaymentStatusNames.AwaitingTransfer,
            PaidAt = DateTime.UtcNow,
            TransactionCode = method == PaymentMethodNames.Momo
                ? BuildMomoOrderId(booking)
                : $"BANK-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}"
        };

        MomoCreatePaymentResponse? momoResponse = null;

        if (method == PaymentMethodNames.Momo)
        {
            booking.PaymentAttempts += 1;
            var momoValidationError = ValidateMomoAmount(payment.Amount);
            if (momoValidationError is not null)
                return BadRequest(new { message = momoValidationError });

            try
            {
                momoResponse = await CreateNewMomoPaymentAsync(booking, payment, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create MoMo sandbox payment for booking {BookingCode}", booking.BookingCode);
                return StatusCode(502, new
                {
                    message = "Could not create the MoMo sandbox payment session. Please try again.",
                    detail = ex.Message
                });
            }

            booking.PaymentStatus = PaymentStatusNames.Pending;
        }
        else
        {
            booking.PaymentStatus = PaymentStatusNames.AwaitingTransfer;
        }

        booking.UpdatedAt = DateTime.UtcNow;

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        var response = MapPayment(payment, momoResponse, _momoOptions.UseSandbox);
        await SendCreatedPaymentEmailAsync(booking, response, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = payment.Id }, response);
    }

    [Authorize(Roles = RoleNames.Customer)]
    [HttpPatch("{id:int}/confirm-transfer")]
    public async Task<ActionResult<PaymentDto>> ConfirmTransfer(int id, CancellationToken cancellationToken)
    {
        var currentEmail = GetCurrentEmail();
        if (string.IsNullOrWhiteSpace(currentEmail))
            return Unauthorized(new { message = "Invalid token" });

        var payment = await _db.Payments
            .Include(x => x.Booking)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (payment == null || payment.Booking == null)
            return NotFound(new { message = "Payment not found" });

        if (!string.Equals(payment.Booking.Email.Trim(), currentEmail, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        if (payment.Method != PaymentMethodNames.BankTransfer)
            return BadRequest(new { message = "Only bank transfer payments can be confirmed manually." });

        if (payment.Status != PaymentStatusNames.AwaitingTransfer)
            return BadRequest(new { message = "This transfer confirmation has already been submitted." });

        payment.Status = PaymentStatusNames.Pending;
        payment.PaidAt = DateTime.UtcNow;
        payment.Booking.PaymentStatus = PaymentStatusNames.Pending;
        payment.Booking.Status = BookingStatusNames.Pending;
        ResetCheckIn(payment.Booking);
        payment.Booking.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        await NotifyCashiersAsync(payment.Booking, payment, cancellationToken);
        await _emailSender.SendAsync(
            payment.Booking.Email,
            "SuSpa transfer confirmation received",
            EmailTemplateService.BuildBankTransferSubmittedTemplate(
                payment.Booking.FullName,
                payment.Booking.BookingCode,
                payment.PaymentCode),
            cancellationToken);

        return Ok(MapPayment(payment));
    }

    [AllowAnonymous]
    [HttpGet("momo/return")]
    public async Task<IActionResult> ReceiveMomoReturn(
        [FromQuery] string? bookingCode,
        [FromQuery] string? paymentCode,
        [FromQuery] string? resultCode,
        [FromQuery] string? orderId,
        [FromQuery] string? requestId,
        [FromQuery] string? transId,
        [FromQuery] string? message,
        CancellationToken cancellationToken)
    {
        await TryApplySandboxReturnFallbackAsync(
            bookingCode,
            paymentCode,
            resultCode,
            orderId,
            transId,
            cancellationToken);

        var frontendUrl = (_momoOptions.FrontendReturnUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(frontendUrl))
            return Content("MoMo return received, but FrontendReturnUrl is not configured.", "text/plain");

        var query = new List<string>();

        if (!string.IsNullOrWhiteSpace(bookingCode)) query.Add($"bookingCode={Uri.EscapeDataString(bookingCode)}");
        if (!string.IsNullOrWhiteSpace(paymentCode)) query.Add($"paymentCode={Uri.EscapeDataString(paymentCode)}");
        if (!string.IsNullOrWhiteSpace(resultCode)) query.Add($"resultCode={Uri.EscapeDataString(resultCode)}");
        if (!string.IsNullOrWhiteSpace(orderId)) query.Add($"orderId={Uri.EscapeDataString(orderId)}");
        if (!string.IsNullOrWhiteSpace(requestId)) query.Add($"requestId={Uri.EscapeDataString(requestId)}");
        if (!string.IsNullOrWhiteSpace(transId)) query.Add($"transId={Uri.EscapeDataString(transId)}");
        if (!string.IsNullOrWhiteSpace(message)) query.Add($"message={Uri.EscapeDataString(message)}");

        var separator = frontendUrl.Contains('?') ? "&" : "?";
        var finalUrl = query.Count > 0
            ? $"{frontendUrl}{separator}{string.Join("&", query)}"
            : frontendUrl;

        return Redirect(finalUrl);
    }

    [AllowAnonymous]
    [HttpPost("momo/ipn")]
    public async Task<IActionResult> ReceiveMomoIpn([FromBody] MomoIpnRequest request, CancellationToken cancellationToken)
    {
        if (!_momoService.IsValidIpnSignature(request))
        {
            _logger.LogWarning("Rejected MoMo IPN because signature is invalid for order {OrderId}", request.OrderId);
            return Unauthorized();
        }

        var payment = await _db.Payments
            .Include(x => x.Booking)
            .FirstOrDefaultAsync(
                x => x.Method == PaymentMethodNames.Momo
                    && x.TransactionCode != null
                    && x.TransactionCode.StartsWith(request.OrderId),
                cancellationToken);

        if (payment == null)
        {
            _logger.LogWarning("Received MoMo IPN for unknown order {OrderId}", request.OrderId);
            return NoContent();
        }

        if (payment.Booking == null)
            return NoContent();

        var expired = payment.PaidAt.Add(PaymentTtl) <= DateTime.UtcNow;
        if (expired)
        {
            payment.Status = PaymentStatusNames.Rejected;
            payment.Booking.PaymentStatus = PaymentStatusNames.Rejected;
            payment.Booking.Status = BookingStatusNames.Pending;
            ResetCheckIn(payment.Booking);
            payment.Booking.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Ignored MoMo IPN because session expired for order {OrderId}", request.OrderId);
            return NoContent();
        }

        if (payment.Amount != request.Amount || !string.Equals(request.PartnerCode, _momoOptions.PartnerCode, StringComparison.Ordinal))
        {
            _logger.LogWarning("Rejected MoMo IPN because amount or partner code mismatched for order {OrderId}", request.OrderId);
            return Unauthorized();
        }

        payment.TransactionCode = $"{request.OrderId}|{request.TransId}";
        payment.PaidAt = DateTime.UtcNow;
        payment.Booking.UpdatedAt = DateTime.UtcNow;

        if (request.ResultCode == 0)
        {
            await ApplySuccessfulPaymentAsync(payment, request.OrderId, request.TransId.ToString(), cancellationToken);
        }
        else
        {
            await ApplyRejectedPaymentAsync(payment, BookingStatusNames.Cancelled, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Cashier}")]
    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<PaymentDto>> UpdateStatus(int id, PaymentStatusUpdateDto dto)
    {
        var status = NormalizeStatus(dto.Status);
        if (!AdminAllowedStatuses.Contains(status))
            return BadRequest(new { message = "Invalid payment status" });

        var payment = await _db.Payments
            .Include(x => x.Booking)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (payment == null || payment.Booking == null)
            return NotFound(new { message = "Payment not found" });

        if (payment.Method == PaymentMethodNames.Momo)
        {
            return BadRequest(new
            {
                message = "MoMo payments are updated automatically after the customer completes the hosted payment page."
            });
        }

        if (payment.Status == PaymentStatusNames.AwaitingTransfer)
        {
            return BadRequest(new
            {
                message = "Wait for the customer to confirm the transfer before cashier review."
            });
        }

        if (payment.Booking.Status == BookingStatusNames.Completed && status != PaymentStatusNames.Paid)
            return BadRequest(new { message = "Completed bookings cannot have their payment downgraded." });

        if (payment.Status == PaymentStatusNames.Paid && status == PaymentStatusNames.Rejected)
            return BadRequest(new { message = "Use REFUNDED instead of REJECTED after payment is already confirmed." });

        payment.Status = status;
        payment.PaidAt = DateTime.UtcNow;
        payment.Booking.PaymentStatus = status;
        payment.Booking.UpdatedAt = DateTime.UtcNow;

        switch (status)
        {
            case PaymentStatusNames.Paid:
                payment.Booking.Status = BookingStatusNames.Confirmed;
                var paidResult = await _bookingStaffingService.AutoAssignAsync(payment.Booking, HttpContext.RequestAborted);
                if (paidResult.HasIncompleteStaffing)
                {
                    _logger.LogInformation(
                        "Booking {BookingCode} paid with incomplete staffing: {Warnings}",
                        payment.Booking.BookingCode,
                        string.Join(" | ", paidResult.Warnings));
                }

                await _emailSender.SendAsync(
                    payment.Booking.Email,
                    "SuSpa payment confirmed",
                    EmailTemplateService.BuildPaymentConfirmedTemplate(
                        payment.Booking.FullName,
                        payment.Booking.BookingCode,
                        payment.PaymentCode));
                break;

            case PaymentStatusNames.Rejected:
                payment.Booking.Status = BookingStatusNames.Pending;
                ResetCheckIn(payment.Booking);

                await _emailSender.SendAsync(
                    payment.Booking.Email,
                    "SuSpa payment rejected",
                    EmailTemplateService.BuildPaymentRejectedTemplate(
                        payment.Booking.FullName,
                        payment.Booking.BookingCode,
                        payment.PaymentCode));
                break;

            case PaymentStatusNames.Refunded:
                payment.Booking.Status = BookingStatusNames.Cancelled;
                ResetCheckIn(payment.Booking);
                break;
        }

        await _db.SaveChangesAsync();
        return Ok(MapPayment(payment));
    }

    [Authorize(Roles = RoleNames.Admin)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var payment = await _db.Payments
            .Include(x => x.Booking)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (payment == null)
            return NotFound(new { message = "Payment not found" });

        if (payment.Booking != null)
        {
            payment.Booking.PaymentStatus = PaymentStatusNames.Unpaid;
            if (payment.Booking.Status == BookingStatusNames.Confirmed)
                payment.Booking.Status = BookingStatusNames.Pending;
            ResetCheckIn(payment.Booking);

            payment.Booking.UpdatedAt = DateTime.UtcNow;
        }

        _db.Payments.Remove(payment);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<ActionResult<PaymentDto>> ReuseOrRefreshMomoPaymentAsync(Booking booking, Payment existingPending, CancellationToken cancellationToken)
    {
        var expired = existingPending.PaidAt.Add(PaymentTtl) <= DateTime.UtcNow;
        if (expired)
        {
            existingPending.Status = PaymentStatusNames.Rejected;
            booking.PaymentStatus = PaymentStatusNames.Rejected;
            booking.Status = BookingStatusNames.Pending;
            ResetCheckIn(booking);
            booking.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return BadRequest(new { message = "The previous MoMo session expired. Please create a new payment request." });
        }

        try
        {
            var refreshedResponse = await CreateNewMomoPaymentAsync(booking, existingPending, cancellationToken);
            booking.PaymentStatus = PaymentStatusNames.Pending;
            booking.UpdatedAt = DateTime.UtcNow;
            existingPending.PaidAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return Ok(MapPayment(existingPending, refreshedResponse, _momoOptions.UseSandbox));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recreate MoMo sandbox payment for booking {BookingCode}", booking.BookingCode);
            return StatusCode(502, new
            {
                message = "Could not create the MoMo sandbox payment session.",
                detail = ex.Message
            });
        }
    }

    private async Task<MomoCreatePaymentResponse> CreateNewMomoPaymentAsync(Booking booking, Payment payment, CancellationToken cancellationToken)
    {
        var redirectUrl = BuildPublicReturnUrl(booking, payment);
        var ipnUrl = BuildIpnUrl();
        var requestId = $"REQ-{payment.PaymentCode}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var orderId = BuildMomoOrderId(booking);
        var extraData = BuildExtraData(booking, payment);

        payment.TransactionCode = orderId;

        var momoRequest = new MomoCreatePaymentRequest
        {
            AccessKey = _momoOptions.AccessKey,
            PartnerCode = _momoOptions.PartnerCode,
            RequestId = requestId,
            Amount = Convert.ToInt64(payment.Amount),
            OrderId = orderId,
            OrderInfo = $"SuSpa payment for {booking.BookingCode}",
            RedirectUrl = redirectUrl,
            IpnUrl = ipnUrl,
            ExtraData = extraData,
            RequestType = "captureWallet",
            Lang = "en",
            PartnerName = string.IsNullOrWhiteSpace(_momoOptions.PartnerName) ? _momoOptions.StoreName : _momoOptions.PartnerName,
            StoreId = string.IsNullOrWhiteSpace(_momoOptions.StoreId) ? "SuSpaStore" : _momoOptions.StoreId,
            StoreName = _momoOptions.StoreName,
            AutoCapture = true,
            UserInfo = new
            {
                name = booking.FullName,
                phoneNumber = booking.Phone,
                email = booking.Email
            }
        };

        var momoResponse = await _momoService.CreatePaymentAsync(momoRequest, cancellationToken);
        if (momoResponse.ResultCode != 0 || string.IsNullOrWhiteSpace(momoResponse.PayUrl))
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(momoResponse.Message)
                ? "MoMo could not create the payment session."
                : momoResponse.Message);
        }

        return momoResponse;
    }

    private async Task SendCreatedPaymentEmailAsync(Booking booking, PaymentDto payment, CancellationToken cancellationToken)
    {
        if (payment.Method == PaymentMethodNames.BankTransfer)
        {
            await _emailSender.SendAsync(
                booking.Email,
                "SuSpa bank transfer instruction",
                EmailTemplateService.BuildBankTransferInstructionTemplate(
                    booking.FullName,
                    booking.BookingCode,
                    payment.PaymentCode,
                    payment.ProviderName,
                    payment.AccountNumber,
                    payment.AccountName,
                    payment.Amount,
                    payment.PaymentContent,
                    _bankTransferOptions.Instruction),
                cancellationToken);
            return;
        }

        await _emailSender.SendAsync(
            booking.Email,
            "SuSpa MoMo payment created",
            EmailTemplateService.BuildPaymentRequestTemplate(
                booking.FullName,
                booking.BookingCode,
                payment.PaymentCode,
                payment.Method,
                payment.Amount,
                payment.PaymentContent),
            cancellationToken);
    }

    private async Task NotifyCashiersAsync(Booking booking, Payment payment, CancellationToken cancellationToken)
    {
        var recipients = await _db.Admins
            .AsNoTracking()
            .Where(x => x.IsActive
                && !string.IsNullOrWhiteSpace(x.Email)
                && (x.Role == RoleNames.Admin || x.Role == RoleNames.Cashier))
            .Select(x => x.Email)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (recipients.Count == 0)
            return;

        var paymentContent = $"{payment.PaymentCode} {booking.BookingCode}".Trim();
        var body = EmailTemplateService.BuildCashierPaymentSubmittedTemplate(
            booking.BookingCode,
            payment.PaymentCode,
            booking.FullName,
            payment.Amount,
            paymentContent);

        foreach (var email in recipients)
        {
            await _emailSender.SendAsync(email, "SuSpa bank transfer needs cashier review", body, cancellationToken);
        }
    }

    private string BuildIpnUrl()
    {
        var configuredBaseUrl = (_momoOptions.PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        var requestBaseUrl = $"{Request.Scheme}://{Request.Host}".TrimEnd('/');
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl) ? requestBaseUrl : configuredBaseUrl;
        var ipnPath = string.IsNullOrWhiteSpace(_momoOptions.IpnPath) ? "/api/payments/momo/ipn" : _momoOptions.IpnPath;

        if (!ipnPath.StartsWith('/'))
            ipnPath = $"/{ipnPath}";

        return $"{baseUrl}{ipnPath}";
    }

    private string BuildPublicReturnUrl(Booking booking, Payment payment)
    {
        var configuredBaseUrl = (_momoOptions.PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        var requestBaseUrl = $"{Request.Scheme}://{Request.Host}".TrimEnd('/');
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl) ? requestBaseUrl : configuredBaseUrl;
        var returnPath = string.IsNullOrWhiteSpace(_momoOptions.ReturnPath) ? "/api/payments/momo/return" : _momoOptions.ReturnPath;
        if (!returnPath.StartsWith('/'))
            returnPath = $"/{returnPath}";

        return $"{baseUrl}{returnPath}?bookingCode={Uri.EscapeDataString(booking.BookingCode)}&paymentCode={Uri.EscapeDataString(payment.PaymentCode)}";
    }

    private static string BuildExtraData(Booking booking, Payment payment)
    {
        var json = JsonSerializer.Serialize(new
        {
            bookingCode = booking.BookingCode,
            paymentCode = payment.PaymentCode,
            bookingId = booking.Id
        });

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private PaymentDto MapPayment(Payment payment, MomoCreatePaymentResponse? momo = null, bool isSandbox = false)
    {
        var paymentContent = $"{payment.PaymentCode} {payment.Booking?.BookingCode ?? string.Empty}".Trim();
        var isMomo = payment.Method == PaymentMethodNames.Momo;

        return new PaymentDto
        {
            Id = payment.Id,
            PaymentCode = payment.PaymentCode,
            BookingId = payment.BookingId,
            BookingCode = payment.Booking?.BookingCode ?? string.Empty,
            Method = payment.Method,
            Amount = payment.Amount,
            Status = payment.Status,
            PaidAt = ToUtc(payment.PaidAt),
            TransactionCode = payment.TransactionCode,
            ProviderName = isMomo ? "MoMo E-Wallet" : _bankTransferOptions.ProviderName,
            AccountNumber = isMomo ? "0901234567" : _bankTransferOptions.AccountNumber,
            AccountName = isMomo ? "CONG TY SUSPA MOMO" : _bankTransferOptions.AccountName,
            PaymentContent = paymentContent,
            QrNote = isMomo
                ? "You will be redirected to the official MoMo sandbox payment page to scan the QR code."
                : _bankTransferOptions.Instruction,
            PayUrl = momo?.PayUrl,
            DeepLink = momo?.Deeplink,
            QrCodeUrl = momo?.QrCodeUrl,
            IsSandbox = isSandbox,
            CustomerCanConfirm = payment.Method == PaymentMethodNames.BankTransfer
                && payment.Status == PaymentStatusNames.AwaitingTransfer,
            RequiresManualReview = payment.Method == PaymentMethodNames.BankTransfer
                && payment.Status == PaymentStatusNames.Pending
        };
    }

    private string? ValidateMomoAmount(decimal amount)
    {
        if (!_momoOptions.Enabled
            || string.IsNullOrWhiteSpace(_momoOptions.PartnerCode)
            || string.IsNullOrWhiteSpace(_momoOptions.AccessKey)
            || string.IsNullOrWhiteSpace(_momoOptions.SecretKey))
        {
            return "MoMo sandbox is not configured yet. Please update appsettings.json with PartnerCode, AccessKey and SecretKey.";
        }

        if (amount is < 1000 or > 50_000_000)
            return "MoMo requires amount between 1,000 VND and 50,000,000 VND. Please update service prices to VND.";

        return null;
    }

    private string? ValidateBookingPaymentWindow(Booking booking)
    {
        var bangkokNow = GetBangkokNow();
        var todayBk = DateOnly.FromDateTime(bangkokNow.Date);

        if (booking.AppointmentDate < todayBk)
            return "This booking date has passed. Payment is no longer available.";

        if (booking.AppointmentDate != todayBk)
            return null;

        var appointmentMinutes = ParseTimeToMinutes(booking.AppointmentTime);
        var nowMinutes = bangkokNow.Hour * 60 + bangkokNow.Minute;
        if (appointmentMinutes.HasValue && appointmentMinutes.Value <= nowMinutes)
            return "Payment closed because the appointment time has passed.";

        return null;
    }

    private static bool IsActivePaymentStatus(string status) =>
        status == PaymentStatusNames.AwaitingTransfer || status == PaymentStatusNames.Pending;

    private async Task TryApplySandboxReturnFallbackAsync(
        string? bookingCode,
        string? paymentCode,
        string? resultCode,
        string? orderId,
        string? transId,
        CancellationToken cancellationToken)
    {
        if (!CanUseSandboxReturnFallback())
            return;

        if (!string.Equals(resultCode, "0", StringComparison.Ordinal))
            return;

        if (string.IsNullOrWhiteSpace(paymentCode) || string.IsNullOrWhiteSpace(bookingCode))
            return;

        var payment = await _db.Payments
            .Include(x => x.Booking)
            .FirstOrDefaultAsync(
                x => x.Method == PaymentMethodNames.Momo
                    && x.PaymentCode == paymentCode
                    && x.Booking != null
                    && x.Booking.BookingCode == bookingCode,
                cancellationToken);

        if (payment?.Booking == null)
            return;

        if (payment.Status == PaymentStatusNames.Paid)
            return;

        if (payment.Status != PaymentStatusNames.Pending)
            return;

        if (!string.IsNullOrWhiteSpace(orderId)
            && !string.IsNullOrWhiteSpace(payment.TransactionCode)
            && !payment.TransactionCode.StartsWith(orderId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Skipped MoMo return fallback because order id {OrderId} does not match payment {PaymentCode}",
                orderId,
                payment.PaymentCode);
            return;
        }

        _logger.LogInformation(
            "Applying sandbox MoMo return fallback for booking {BookingCode} payment {PaymentCode}",
            bookingCode,
            paymentCode);

        await ApplySuccessfulPaymentAsync(payment, orderId, transId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private bool CanUseSandboxReturnFallback()
    {
        if (!_momoOptions.UseSandbox)
            return false;

        if (!string.IsNullOrWhiteSpace(_momoOptions.PublicBaseUrl))
            return false;

        var host = Request.Host.Host?.Trim().ToLowerInvariant() ?? string.Empty;
        return host is "localhost" or "127.0.0.1";
    }

    private async Task ApplySuccessfulPaymentAsync(
        Payment payment,
        string? orderId,
        string? transId,
        CancellationToken cancellationToken)
    {
        if (payment.Booking == null)
            return;

        payment.TransactionCode = !string.IsNullOrWhiteSpace(transId)
            ? $"{orderId}|{transId}"
            : orderId ?? payment.TransactionCode;
        payment.PaidAt = DateTime.UtcNow;
        payment.Status = PaymentStatusNames.Paid;
        payment.Booking.PaymentStatus = PaymentStatusNames.Paid;
        payment.Booking.Status = BookingStatusNames.Confirmed;
        payment.Booking.UpdatedAt = DateTime.UtcNow;

        var staffingResult = await _bookingStaffingService.AutoAssignAsync(payment.Booking, cancellationToken);
        if (staffingResult.HasIncompleteStaffing)
        {
            _logger.LogInformation(
                "Booking {BookingCode} confirmed through payment with incomplete staffing: {Warnings}",
                payment.Booking.BookingCode,
                string.Join(" | ", staffingResult.Warnings));
        }

        await _emailSender.SendAsync(
            payment.Booking.Email,
            "SuSpa payment confirmed",
            EmailTemplateService.BuildPaymentConfirmedTemplate(
                payment.Booking.FullName,
                payment.Booking.BookingCode,
                payment.PaymentCode),
            cancellationToken);
    }

    private async Task ApplyRejectedPaymentAsync(
        Payment payment,
        string bookingStatus,
        CancellationToken cancellationToken)
    {
        if (payment.Booking == null)
            return;

        payment.Status = PaymentStatusNames.Rejected;
        payment.Booking.PaymentStatus = PaymentStatusNames.Rejected;
        payment.Booking.Status = bookingStatus;
        ResetCheckIn(payment.Booking);
        payment.Booking.UpdatedAt = DateTime.UtcNow;

        await _emailSender.SendAsync(
            payment.Booking.Email,
            "SuSpa payment rejected",
            EmailTemplateService.BuildPaymentRejectedTemplate(
                payment.Booking.FullName,
                payment.Booking.BookingCode,
                payment.PaymentCode),
            cancellationToken);
    }

    private bool CanAccessPayment(Booking? booking) =>
        booking != null && CanAccessBooking(booking);

    private bool CanAccessBooking(Booking booking)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (role == RoleNames.Admin || role == RoleNames.Cashier)
            return true;

        var currentEmail = GetCurrentEmail();
        return !string.IsNullOrWhiteSpace(currentEmail)
            && string.Equals(booking.Email.Trim(), currentEmail, StringComparison.OrdinalIgnoreCase);
    }

    private string GetCurrentEmail() =>
        User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string NormalizeMethod(string? method) =>
        (method ?? string.Empty).Trim().ToUpperInvariant();

    private static string NormalizeStatus(string? status) =>
        (status ?? string.Empty).Trim().ToUpperInvariant();

    private static string BuildMomoOrderId(Booking booking) =>
        $"MOMO-{booking.BookingCode}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

    private static DateTime ToUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc) return value;
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private static void ResetCheckIn(Booking booking)
    {
        booking.IsCheckedIn = false;
        booking.CheckedInAt = null;
    }

    private static DateTime GetBangkokNow()
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch
        {
            return DateTime.UtcNow.AddHours(7);
        }
    }

    private static int? ParseTimeToMinutes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, out var dt))
            return dt.Hour * 60 + dt.Minute;

        var parts = value.Split(':');
        if (parts.Length >= 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1].Substring(0, 2), out var m))
            return (h % 24) * 60 + Math.Clamp(m, 0, 59);

        return null;
    }
}
