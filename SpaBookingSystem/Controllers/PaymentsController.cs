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
using SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.DataLayer; 

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private static readonly string[] AllowedMethods = ["MOMO", "BANK_TRANSFER", "CARD", "WALLET"];
    private static readonly string[] AdminAllowedStatuses = ["PENDING", "PAID", "REJECTED", "REFUNDED"];
    private static readonly TimeSpan PaymentTtl = TimeSpan.FromMinutes(5);

    private readonly SpaDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IMomoService _momoService;
    private readonly MomoOptions _momoOptions;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        SpaDbContext db,
        IEmailSender emailSender,
        IMomoService momoService,
        IOptions<MomoOptions> momoOptions,
        ILogger<PaymentsController> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _momoService = momoService;
        _momoOptions = momoOptions.Value;
        _logger = logger;
    }

    [Authorize(Roles = "ADMIN")]
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
        {
            return NotFound(new { message = "Payment not found" });
        }

        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var currentEmail = User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        if (role != "ADMIN" && entity.Booking?.Email.Trim().ToLowerInvariant() != currentEmail)
        {
            return Forbid();
        }

        return Ok(MapPayment(entity));
    }

    [Authorize(Roles = "CUSTOMER")]
    [HttpPost]
    public async Task<ActionResult<PaymentDto>> Create(PaymentCreateDto dto, CancellationToken cancellationToken)
    {
        string? redirectUrl = null;
        string? ipnUrl = null;

        var method = (dto.Method ?? string.Empty).Trim().ToUpperInvariant();
        if (!AllowedMethods.Contains(method))
        {
            return BadRequest(new { message = "Invalid payment method" });
        }

        var currentEmail = User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(currentEmail))
        {
            return Unauthorized(new { message = "Invalid token" });
        }

        var booking = await _db.Bookings
            .Include(x => x.Payments)
            .Include(x => x.BookingDetails)
            .ThenInclude(x => x.Service)
            .FirstOrDefaultAsync(x => x.Id == dto.BookingId, cancellationToken);

        if (booking == null)
        {
            return NotFound(new { message = "Booking not found" });
        }

        if (booking.Email.Trim().ToLowerInvariant() != currentEmail)
        {
            return Forbid();
        }

        var existingPending = booking.Payments
            .FirstOrDefault(p => p.Method == "MOMO" && p.Status == "PENDING");

        // If there is an existing MoMo pending payment, either reuse (if still valid) or mark rejected then create new.
        if (existingPending != null)
        {
            var expired = existingPending.PaidAt.Add(PaymentTtl) <= DateTime.UtcNow;
            if (expired)
            {
                existingPending.Status = "REJECTED";
                booking.PaymentStatus = "REJECTED";
                if (booking.Status == "CONFIRMED")
                {
                    booking.Status = "PENDING";
                }
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                string? redirect = null;
                string? ipn = null;
                try
                {
                    redirect = BuildPublicReturnUrl(booking, existingPending);
                    ipn = BuildIpnUrl();
                    var extraData = BuildExtraData(booking, existingPending);
                    var requestId = $"REQ-{existingPending.PaymentCode}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                    var orderIdReuse = existingPending.TransactionCode ?? $"MOMO-{booking.BookingCode}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                    existingPending.TransactionCode = orderIdReuse;

                    var momoRequestReuse = new MomoCreatePaymentRequest
                    {
                        AccessKey = _momoOptions.AccessKey,
                        PartnerCode = _momoOptions.PartnerCode,
                        RequestId = requestId,
                        Amount = Convert.ToInt64(existingPending.Amount),
                        OrderId = orderIdReuse,
                        OrderInfo = $"SuSpa payment for {booking.BookingCode}",
                        RedirectUrl = redirect,
                        IpnUrl = ipn,
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

                    var momoResponseReuse = await _momoService.CreatePaymentAsync(momoRequestReuse, cancellationToken);
                    if (momoResponseReuse.ResultCode != 0 || string.IsNullOrWhiteSpace(momoResponseReuse.PayUrl))
                    {
                        return BadRequest(new
                        {
                            message = string.IsNullOrWhiteSpace(momoResponseReuse.Message)
                                ? "MoMo could not create the payment session."
                                : momoResponseReuse.Message,
                            resultCode = momoResponseReuse.ResultCode
                        });
                    }

                    booking.PaymentStatus = "PENDING";
                    booking.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(cancellationToken);

                    var reuseResponse = MapPayment(existingPending, momoResponseReuse, _momoOptions.UseSandbox);
                    reuseResponse.BookingCode = booking.BookingCode;
                    return Ok(reuseResponse);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to recreate MoMo sandbox payment for booking {BookingCode}. OrderId={OrderId}, RedirectUrl={RedirectUrl}, IpnUrl={IpnUrl}",
                        booking.BookingCode,
                        existingPending.TransactionCode,
                        redirect,
                        ipn);

                    return StatusCode(502, new
                    {
                        message = "Could not create the MoMo sandbox payment session.",
                        detail = ex.Message
                    });
                }
            }
        }

        if (booking.PaymentStatus == "PAID" || booking.PaymentStatus == "PENDING")
        {
            return BadRequest(new { message = "This booking already has a payment request" });
        }

        var paymentCode = $"PAY-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var orderId = $"MOMO-{booking.BookingCode}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var momoAmount = Convert.ToInt64(decimal.Round(booking.TotalAmount * 1000m, 0, MidpointRounding.AwayFromZero));
        var payment = new Payment
        {
            BookingId = booking.Id,
            PaymentCode = paymentCode,
            Method = method,
            Amount = method == "MOMO"
                ? momoAmount
                : booking.TotalAmount,
            Status = "PENDING",
            PaidAt = DateTime.UtcNow,
            TransactionCode = method == "MOMO"
                ? orderId
                : $"TXN-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}"
        };

        MomoCreatePaymentResponse? momoResponse = null;

        if (method == "MOMO")
        {
            if (!_momoOptions.Enabled
                || string.IsNullOrWhiteSpace(_momoOptions.PartnerCode)
                || string.IsNullOrWhiteSpace(_momoOptions.AccessKey)
                || string.IsNullOrWhiteSpace(_momoOptions.SecretKey))
            {
                return BadRequest(new { message = "MoMo sandbox is not configured yet. Please update appsettings.json with PartnerCode, AccessKey and SecretKey." });
            }

            try
            {
                redirectUrl = BuildPublicReturnUrl(booking, payment);
                ipnUrl = BuildIpnUrl();
                var extraData = BuildExtraData(booking, payment);
                var requestId = $"REQ-{payment.PaymentCode}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

                var momoRequest = new MomoCreatePaymentRequest
                {
                    AccessKey = _momoOptions.AccessKey,
                    PartnerCode = _momoOptions.PartnerCode,
                    RequestId = requestId,
                    Amount = momoAmount,
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

                momoResponse = await _momoService.CreatePaymentAsync(momoRequest, cancellationToken);
                if (momoResponse.ResultCode != 0 || string.IsNullOrWhiteSpace(momoResponse.PayUrl))
                {
                    return BadRequest(new
                    {
                        message = string.IsNullOrWhiteSpace(momoResponse.Message)
                            ? "MoMo could not create the payment session."
                            : momoResponse.Message,
                        resultCode = momoResponse.ResultCode
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to create MoMo sandbox payment for booking {BookingCode}. OrderId={OrderId}, RedirectUrl={RedirectUrl}, IpnUrl={IpnUrl}",
                    booking.BookingCode,
                    orderId,
                    redirectUrl,
                    ipnUrl);

                return StatusCode(502, new
                {
                    message = "Could not create the MoMo sandbox payment session.",
                    detail = ex.Message
                });
            }
        }

        _db.Payments.Add(payment);
        booking.PaymentStatus = "PENDING";
        booking.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var response = MapPayment(payment, momoResponse, _momoOptions.UseSandbox);
        response.BookingCode = booking.BookingCode;

        await _emailSender.SendAsync(
            booking.Email,
            method == "MOMO" ? "SuSpa MoMo payment created" : "SuSpa payment request created",
            EmailTemplateService.BuildPaymentRequestTemplate(
                booking.FullName,
                booking.BookingCode,
                payment.PaymentCode,
                payment.Method,
                payment.Amount,
                response.PaymentContent),
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = payment.Id }, response);
    }

    [AllowAnonymous]
    [HttpGet("momo/return")]
    public IActionResult ReceiveMomoReturn(
        [FromQuery] string? bookingCode,
        [FromQuery] string? paymentCode,
        [FromQuery] string? resultCode,
        [FromQuery] string? orderId,
        [FromQuery] string? requestId,
        [FromQuery] string? transId,
        [FromQuery] string? message)
    {
        var frontendUrl = (_momoOptions.FrontendReturnUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(frontendUrl))
        {
            return Content("MoMo return received, but FrontendReturnUrl is not configured.", "text/plain");
        }

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
                x => x.Method == "MOMO"
                    && x.TransactionCode != null
                    && x.TransactionCode.StartsWith(request.OrderId),
                cancellationToken);

        if (payment == null)
        {
            _logger.LogWarning("Received MoMo IPN for unknown order {OrderId}", request.OrderId);
            return NoContent();
        }

        if (payment.Booking == null)
        {
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
            payment.Status = "PAID";
            payment.Booking.PaymentStatus = "PAID";
            payment.Booking.Status = "CONFIRMED";

            await _emailSender.SendAsync(
                payment.Booking.Email,
                "SuSpa payment confirmed",
                EmailTemplateService.BuildPaymentConfirmedTemplate(
                    payment.Booking.FullName,
                    payment.Booking.BookingCode,
                    payment.PaymentCode),
                cancellationToken);
        }
        else
        {
            payment.Status = "REJECTED";
            payment.Booking.PaymentStatus = "REJECTED";
            if (payment.Booking.Status == "CONFIRMED")
            {
                payment.Booking.Status = "PENDING";
            }

            await _emailSender.SendAsync(
                payment.Booking.Email,
                "SuSpa payment rejected",
                EmailTemplateService.BuildPaymentRejectedTemplate(
                    payment.Booking.FullName,
                    payment.Booking.BookingCode,
                    payment.PaymentCode),
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<PaymentDto>> UpdateStatus(int id, PaymentStatusUpdateDto dto)
    {
        var status = (dto.Status ?? string.Empty).Trim().ToUpperInvariant();
        if (!AdminAllowedStatuses.Contains(status))
        {
            return BadRequest(new { message = "Invalid payment status" });
        }

        var payment = await _db.Payments
            .Include(x => x.Booking)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (payment == null)
        {
            return NotFound(new { message = "Payment not found" });
        }

        if (string.Equals(payment.Method, "MOMO", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "MoMo payments are updated automatically after the customer completes the hosted payment page." });
        }

        payment.Status = status;

        if (payment.Booking != null)
        {
            payment.Booking.PaymentStatus = status;
            payment.Booking.UpdatedAt = DateTime.UtcNow;

            if (status == "PAID")
            {
                payment.Booking.Status = "CONFIRMED";
                await _emailSender.SendAsync(
                    payment.Booking.Email,
                    "SuSpa payment confirmed",
                    EmailTemplateService.BuildPaymentConfirmedTemplate(
                        payment.Booking.FullName,
                        payment.Booking.BookingCode,
                        payment.PaymentCode));
            }
            else if (status == "REJECTED")
            {
                if (payment.Booking.Status == "CONFIRMED")
                {
                    payment.Booking.Status = "PENDING";
                }

                await _emailSender.SendAsync(
                    payment.Booking.Email,
                    "SuSpa payment rejected",
                    EmailTemplateService.BuildPaymentRejectedTemplate(
                        payment.Booking.FullName,
                        payment.Booking.BookingCode,
                        payment.PaymentCode));
            }
        }

        await _db.SaveChangesAsync();
        return Ok(MapPayment(payment));
    }

    [Authorize(Roles = "ADMIN")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var payment = await _db.Payments
            .Include(x => x.Booking)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (payment == null)
        {
            return NotFound(new { message = "Payment not found" });
        }

        if (payment.Booking != null)
        {
            payment.Booking.PaymentStatus = "UNPAID";
            if (payment.Booking.Status == "CONFIRMED")
            {
                payment.Booking.Status = "PENDING";
            }
            payment.Booking.UpdatedAt = DateTime.UtcNow;
        }

        _db.Payments.Remove(payment);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private string BuildIpnUrl()
    {
        var configuredBaseUrl = (_momoOptions.PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        var requestBaseUrl = $"{Request.Scheme}://{Request.Host}".TrimEnd('/');
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl) ? requestBaseUrl : configuredBaseUrl;
        var ipnPath = string.IsNullOrWhiteSpace(_momoOptions.IpnPath) ? "/api/payments/momo/ipn" : _momoOptions.IpnPath;

        if (!ipnPath.StartsWith('/'))
        {
            ipnPath = $"/{ipnPath}";
        }

        return $"{baseUrl}{ipnPath}";
    }

    private string BuildPublicReturnUrl(Booking booking, Payment payment)
    {
        var configuredBaseUrl = (_momoOptions.PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        var requestBaseUrl = $"{Request.Scheme}://{Request.Host}".TrimEnd('/');
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl) ? requestBaseUrl : configuredBaseUrl;
        var returnPath = string.IsNullOrWhiteSpace(_momoOptions.ReturnPath) ? "/api/payments/momo/return" : _momoOptions.ReturnPath;
        if (!returnPath.StartsWith('/'))
        {
            returnPath = $"/{returnPath}";
        }

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

    private static PaymentDto MapPayment(Payment payment, MomoCreatePaymentResponse? momo = null, bool isSandbox = false)
    {
        var providerName = payment.Method switch
        {
            "MOMO" => "MoMo E-Wallet",
            "BANK_TRANSFER" => "ACB Bank",
            "CARD" => "Card Gateway",
            "WALLET" => "Digital Wallet",
            _ => payment.Method
        };

        var accountNumber = payment.Method == "MOMO" ? "0901234567" : "123456789";
        var accountName = payment.Method == "MOMO" ? "CONG TY SUSPA MOMO" : "CONG TY TNHH SUSPA";
        var paymentContent = $"{payment.PaymentCode} {payment.Booking?.BookingCode ?? string.Empty}".Trim();
        var qrNote = payment.Method == "MOMO"
            ? "You will be redirected to the official MoMo sandbox payment page to scan the QR code."
            : "Transfer to the bank account shown below and use the exact transfer content so admin can verify the payment.";

        return new PaymentDto
        {
            Id = payment.Id,
            PaymentCode = payment.PaymentCode,
            BookingId = payment.BookingId,
            BookingCode = payment.Booking?.BookingCode ?? string.Empty,
            Method = payment.Method,
            Amount = payment.Amount,
            Status = payment.Status,
            PaidAt = payment.PaidAt,
            TransactionCode = payment.TransactionCode,
            ProviderName = providerName,
            AccountNumber = accountNumber,
            AccountName = accountName,
            PaymentContent = paymentContent,
            QrNote = qrNote,
            PayUrl = momo?.PayUrl,
            DeepLink = momo?.Deeplink,
            QrCodeUrl = momo?.QrCodeUrl,
            IsSandbox = isSandbox
        };
    }
}
