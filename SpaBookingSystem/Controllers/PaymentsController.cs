using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpaBookingSystem.Api.Dtos.Payments;
using SpaBookingSystem.Api.Options;
using SpaBookingSystem.ApplicationCore.Constants;
using SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.DataLayer;
using SpaBookingSystem.Services.Bookings;
using SpaBookingSystem.Services.Email;
using SpaBookingSystem.Services.Momo;
using SpaBookingSystem.Services.Options;

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    // Payment method/status guards stay in the controller because they shape
    // the HTTP contract and determine which operational actions are legal.
    private static readonly string[] AllowedMethods = [PaymentMethodNames.Momo, PaymentMethodNames.BankTransfer];
    private static readonly string[] AdminAllowedStatuses =
    [
        PaymentStatusNames.Pending,
        PaymentStatusNames.Paid,
        PaymentStatusNames.Rejected
    ];

    private readonly SpaDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IMomoService _momoService;
    private readonly MomoOptions _momoOptions;
    private readonly BankTransferOptions _bankTransferOptions;
    private readonly IBookingStaffingService _bookingStaffingService;
    private readonly IBookingStatusService _bookingStatusService;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        SpaDbContext db,
        IEmailSender emailSender,
        IMomoService momoService,
        IOptions<MomoOptions> momoOptions,
        IOptions<BankTransferOptions> bankTransferOptions,
        IBookingStaffingService bookingStaffingService,
        IBookingStatusService bookingStatusService,
        ILogger<PaymentsController> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _momoService = momoService;
        _momoOptions = momoOptions.Value;
        _bankTransferOptions = bankTransferOptions.Value;
        _bookingStaffingService = bookingStaffingService;
        _bookingStatusService = bookingStatusService;
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

        // Keep one active payment conversation at a time per booking so the
        // customer cannot create overlapping payment attempts accidentally.
        if (latestActivePayment != null)
        {
            if (latestActivePayment.Method == PaymentMethodNames.Momo && method == PaymentMethodNames.Momo)
                return await ReuseOrRefreshMomoPaymentAsync(booking, latestActivePayment, cancellationToken);

            if (latestActivePayment.Method == PaymentMethodNames.BankTransfer && method == PaymentMethodNames.BankTransfer)
                return Ok(MapPayment(latestActivePayment));

            if (latestActivePayment.Method == PaymentMethodNames.Momo && method == PaymentMethodNames.BankTransfer)
            {
                latestActivePayment.Status = PaymentStatusNames.Rejected;
                latestActivePayment.PaidAt = DateTime.UtcNow;
                booking.PaymentStatus = PaymentStatusNames.Rejected;
                booking.Status = BookingStatusNames.Pending;
                _bookingStatusService.ResetCheckIn(booking);
                booking.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                return BadRequest(new { message = "This booking already has an active payment request." });
            }
        }

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
        _bookingStatusService.ResetCheckIn(payment.Booking);
        payment.Booking.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        await NotifyCashiersAsync(payment.Booking, payment, cancellationToken);
        await TrySendEmailAsync(
            payment.Booking.Email,
            "SuSpa transfer confirmation received",
            EmailTemplateService.BuildBankTransferSubmittedTemplate(
                payment.Booking.FullName,
                payment.Booking.BookingCode,
                payment.PaymentCode),
            cancellationToken,
            $"sending transfer confirmation email for booking {payment.Booking.BookingCode}");

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
            return BadRequest(new { message = "Use the refund action after payment is already confirmed." });

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

                await NotifyAssignedStaffForBookingAsync(payment.Booking, CancellationToken.None);

                await TrySendEmailAsync(
                    payment.Booking.Email,
                    "SuSpa payment confirmed",
                    EmailTemplateService.BuildPaymentConfirmedTemplate(
                        payment.Booking.FullName,
                        payment.Booking.BookingCode,
                        payment.PaymentCode),
                    CancellationToken.None,
                    $"sending payment confirmed email for booking {payment.Booking.BookingCode}");
                break;

            case PaymentStatusNames.Rejected:
                payment.Booking.Status = BookingStatusNames.Pending;
                _bookingStatusService.ResetCheckIn(payment.Booking);

                await TrySendEmailAsync(
                    payment.Booking.Email,
                    "SuSpa payment rejected",
                    EmailTemplateService.BuildPaymentRejectedTemplate(
                        payment.Booking.FullName,
                        payment.Booking.BookingCode,
                        payment.PaymentCode),
                    CancellationToken.None,
                    $"sending payment rejected email for booking {payment.Booking.BookingCode}");
                break;
        }

        await _db.SaveChangesAsync();
        return Ok(MapPayment(payment));
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Cashier}")]
    [HttpPost("{id:int}/refund")]
    public async Task<ActionResult<PaymentDto>> Refund(int id, PaymentRefundDto dto)
    {
        var payment = await _db.Payments
            .Include(x => x.Booking)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (payment == null || payment.Booking == null)
            return NotFound(new { message = "Payment not found" });

        var reason = (dto.Reason ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
            return BadRequest(new { message = "Refund reason is required." });

        if (payment.Status != PaymentStatusNames.Paid)
            return BadRequest(new { message = "Only paid payments can be refunded." });

        if (!CanRefund(payment))
            return BadRequest(new { message = "Only paid bookings that have not been checked in or completed can be refunded." });

        payment.Status = PaymentStatusNames.Refunded;
        payment.RefundReason = reason;
        payment.PaidAt = DateTime.UtcNow;
        payment.Booking.PaymentStatus = PaymentStatusNames.Refunded;
        payment.Booking.Status = BookingStatusNames.Cancelled;
        _bookingStatusService.ResetCheckIn(payment.Booking);
        payment.Booking.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await TrySendEmailAsync(
            payment.Booking.Email,
            "SuSpa payment refunded",
            EmailTemplateService.BuildPaymentRefundedTemplate(
                payment.Booking.FullName,
                payment.Booking.BookingCode,
                payment.PaymentCode,
                reason),
            CancellationToken.None,
            $"sending refund email for booking {payment.Booking.BookingCode}");

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
            _bookingStatusService.ResetCheckIn(payment.Booking);

            payment.Booking.UpdatedAt = DateTime.UtcNow;
        }

        _db.Payments.Remove(payment);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<ActionResult<PaymentDto>> ReuseOrRefreshMomoPaymentAsync(Booking booking, Payment existingPending, CancellationToken cancellationToken)
    {
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
            await TrySendEmailAsync(
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
                cancellationToken,
                $"sending bank transfer instruction for booking {booking.BookingCode}");
            return;
        }

        await TrySendEmailAsync(
            booking.Email,
            "SuSpa MoMo payment created",
            EmailTemplateService.BuildPaymentRequestTemplate(
                booking.FullName,
                booking.BookingCode,
                payment.PaymentCode,
                payment.Method,
                payment.Amount,
                payment.PaymentContent),
            cancellationToken,
            $"sending MoMo payment email for booking {booking.BookingCode}");
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
            await TrySendEmailAsync(
                email,
                "SuSpa bank transfer needs cashier review",
                body,
                cancellationToken,
                $"notifying cashier {email} about payment {payment.PaymentCode}");
        }
    }

    private async Task NotifyAssignedStaffForBookingAsync(Booking booking, CancellationToken cancellationToken)
    {
        // Payment confirmation can auto-assign staff, so notifications are sent
        // after the booking graph is reloaded with the final assignment state.
        await _db.Entry(booking)
            .Collection(x => x.BookingDetails)
            .Query()
            .Include(x => x.Service)
            .Include(x => x.StaffAssignments)
                .ThenInclude(x => x.Staff)
            .LoadAsync(cancellationToken);

        foreach (var detail in booking.BookingDetails.Where(x => x.Service != null))
        {
            foreach (var assignment in detail.StaffAssignments)
            {
                if (assignment.Staff == null || string.IsNullOrWhiteSpace(assignment.Staff.Email))
                    continue;

                await TrySendEmailAsync(
                    assignment.Staff.Email,
                    "SuSpa service assignment",
                    EmailTemplateService.BuildStaffAssignedTemplate(
                        assignment.Staff.FullName,
                        booking.BookingCode,
                        detail.Service!.Name,
                        $"{detail.AppointmentDate:dd/MM/yyyy}",
                        detail.AppointmentTime ?? string.Empty,
                        assignment.AssignedQuantity,
                        booking.FullName,
                        booking.Phone,
                        booking.Email),
                    cancellationToken,
                    $"notifying staff {assignment.Staff.Email} about assignment for booking {booking.BookingCode}");
            }
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
                && payment.Status == PaymentStatusNames.Pending,
            CanRefund = CanRefund(payment),
            RefundReason = payment.RefundReason
        };
    }

    private static bool CanRefund(Payment payment) =>
        payment.Booking != null
        && payment.Status == PaymentStatusNames.Paid
        && payment.Booking.Status != BookingStatusNames.Completed
        && payment.Booking.Status != BookingStatusNames.Cancelled
        && !payment.Booking.IsCheckedIn;

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

        // Auto-assignment is best-effort: incomplete staffing should not block
        // a successful payment from being recorded and confirmed.
        var staffingResult = await _bookingStaffingService.AutoAssignAsync(payment.Booking, cancellationToken);
        if (staffingResult.HasIncompleteStaffing)
        {
            _logger.LogInformation(
                "Booking {BookingCode} confirmed through payment with incomplete staffing: {Warnings}",
                payment.Booking.BookingCode,
                string.Join(" | ", staffingResult.Warnings));
        }

        await NotifyAssignedStaffForBookingAsync(payment.Booking, cancellationToken);

        await TrySendEmailAsync(
            payment.Booking.Email,
            "SuSpa payment confirmed",
            EmailTemplateService.BuildPaymentConfirmedTemplate(
                payment.Booking.FullName,
                payment.Booking.BookingCode,
                payment.PaymentCode),
            cancellationToken,
            $"sending successful MoMo payment email for booking {payment.Booking.BookingCode}");
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
        _bookingStatusService.ResetCheckIn(payment.Booking);
        payment.Booking.UpdatedAt = DateTime.UtcNow;

        await TrySendEmailAsync(
            payment.Booking.Email,
            "SuSpa payment rejected",
            EmailTemplateService.BuildPaymentRejectedTemplate(
                payment.Booking.FullName,
                payment.Booking.BookingCode,
                payment.PaymentCode),
            cancellationToken,
            $"sending rejected payment email for booking {payment.Booking.BookingCode}");
    }

    private async Task<bool> TrySendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken,
        string operation)
    {
        try
        {
            await _emailSender.SendAsync(toEmail, subject, htmlBody, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email send failed while {Operation}", operation);
            return false;
        }
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
