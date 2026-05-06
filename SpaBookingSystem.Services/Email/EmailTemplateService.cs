namespace SpaBookingSystem.Services.Email;

public static class EmailTemplateService
{
    // Keep email bodies centralized here so workflow controllers only decide
    // when a notification should be sent, not how the HTML is composed.
    private static string WrapHtml(string bodyContent)
    {
        return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
  <title>SuSpa Notification</title>
</head>
<body style=""font-family: Georgia, 'Times New Roman', serif; color: #1f1f1f; line-height: 1.6;"">
{bodyContent}
</body>
</html>";
    }

    public static string BuildRegisterTemplate(string fullName, string code)
    {
        return WrapHtml($@"
<h2>Welcome to SuSpa</h2>
<p>Hello <strong>{fullName}</strong>,</p>
<p>Your account has been created successfully.</p>
<p>Your registration code is: <strong>{code}</strong></p>
<p>Please keep this code for support or future verification.</p>");
    }

    public static string BuildPaymentRequestTemplate(string fullName, string bookingCode, string paymentCode, string method, decimal amount, string content)
    {
        return WrapHtml($@"
<h2>Payment request received</h2>
<p>Hello <strong>{fullName}</strong>,</p>
<p>We have received your payment request for booking <strong>{bookingCode}</strong>.</p>
<ul>
  <li>Payment code: <strong>{paymentCode}</strong></li>
  <li>Method: <strong>{method}</strong></li>
  <li>Amount: <strong>{amount:N0}</strong></li>
  <li>Transfer content: <strong>{content}</strong></li>
</ul>
<p>Your order is waiting for admin confirmation.</p>");
    }

    public static string BuildBankTransferInstructionTemplate(string fullName, string bookingCode, string paymentCode, string providerName, string accountNumber, string accountName, decimal amount, string content, string instruction)
    {
        return WrapHtml($@"
<h2>Bank transfer instruction</h2>
<p>Hello <strong>{fullName}</strong>,</p>
<p>Your payment request for booking <strong>{bookingCode}</strong> has been created.</p>
<ul>
  <li>Payment code: <strong>{paymentCode}</strong></li>
  <li>Bank: <strong>{providerName}</strong></li>
  <li>Account number: <strong>{accountNumber}</strong></li>
  <li>Account name: <strong>{accountName}</strong></li>
  <li>Amount: <strong>{amount:N0}</strong></li>
  <li>Transfer content: <strong>{content}</strong></li>
</ul>
<p>{instruction}</p>");
    }

    public static string BuildCashierPaymentSubmittedTemplate(string bookingCode, string paymentCode, string customerName, decimal amount, string content)
    {
        return WrapHtml($@"
<h2>Customer submitted bank transfer</h2>
<p>A customer has confirmed a bank transfer and is waiting for cashier review.</p>
<ul>
  <li>Booking code: <strong>{bookingCode}</strong></li>
  <li>Payment code: <strong>{paymentCode}</strong></li>
  <li>Customer: <strong>{customerName}</strong></li>
  <li>Amount: <strong>{amount:N0}</strong></li>
  <li>Transfer content: <strong>{content}</strong></li>
</ul>
<p>Please review the incoming transfer and update the payment status.</p>");
    }

    public static string BuildBankTransferSubmittedTemplate(string fullName, string bookingCode, string paymentCode)
    {
        return WrapHtml($@"
<h2>Transfer confirmation received</h2>
<p>Hello <strong>{fullName}</strong>,</p>
<p>We have received your transfer confirmation for booking <strong>{bookingCode}</strong>.</p>
<p>Payment code: <strong>{paymentCode}</strong></p>
<p>The cashier will review the transfer and update your booking once it is verified.</p>");
    }

    public static string BuildPaymentConfirmedTemplate(string fullName, string bookingCode, string paymentCode)
    {
        return WrapHtml($@"
<h2>Payment confirmed</h2>
<p>Hello <strong>{fullName}</strong>,</p>
<p>Your payment for booking <strong>{bookingCode}</strong> has been confirmed.</p>
<p>Payment code: <strong>{paymentCode}</strong></p>
<p>Your booking status is now <strong>CONFIRMED</strong>.</p>");
    }

    public static string BuildBookingCompletedTemplate(string fullName, string bookingCode)
    {
        return WrapHtml($@"
<h2>Booking completed</h2>
<p>Hello <strong>{fullName}</strong>,</p>
<p>Your appointment for booking <strong>{bookingCode}</strong> has been marked as completed.</p>
<p>Thank you for visiting SuSpa. We hope to welcome you again soon.</p>");
    }

    public static string BuildPaymentRefundedTemplate(string fullName, string bookingCode, string paymentCode, string refundReason)
    {
        return WrapHtml($@"
<h2>Payment refunded</h2>
<p>Hello <strong>{fullName}</strong>,</p>
<p>Your payment for booking <strong>{bookingCode}</strong> has been refunded.</p>
<ul>
  <li>Payment code: <strong>{paymentCode}</strong></li>
  <li>Refund reason: <strong>{refundReason}</strong></li>
</ul>
<p>Your booking is now closed. If you need a new appointment, please create a new booking from the website.</p>");
    }

    public static string BuildPaymentRejectedTemplate(string fullName, string bookingCode, string paymentCode)
    {
        return WrapHtml($@"
<h2>Payment rejected</h2>
<p>Hello <strong>{fullName}</strong>,</p>
<p>Your payment for booking <strong>{bookingCode}</strong> could not be confirmed.</p>
<p>Payment code: <strong>{paymentCode}</strong></p>
<p>Please review your transfer and submit payment again.</p>");
    }

    public static string BuildCashierNewBookingTemplate(
        string bookingCode,
        string customerName,
        string customerPhone,
        string customerEmail,
        string appointmentDate,
        string appointmentTime,
        decimal totalAmount,
        bool isGroupBooking,
        int groupSize)
    {
        var bookingScope = isGroupBooking
            ? $"Group booking for <strong>{groupSize}</strong> people"
            : "Personal booking";

        return WrapHtml($@"
<h2>New booking created</h2>
<p>A new booking has just been created and is ready for operational follow-up.</p>
<ul>
  <li>Booking code: <strong>{bookingCode}</strong></li>
  <li>Customer: <strong>{customerName}</strong></li>
  <li>Phone: <strong>{customerPhone}</strong></li>
  <li>Email: <strong>{customerEmail}</strong></li>
  <li>Appointment date: <strong>{appointmentDate}</strong></li>
  <li>Appointment time: <strong>{appointmentTime}</strong></li>
  <li>Total amount: <strong>{totalAmount:N0}</strong></li>
  <li>Booking type: <strong>{bookingScope}</strong></li>
</ul>
<p>Please monitor payment progress and prepare the booking workflow when needed.</p>");
    }

    public static string BuildStaffAssignedTemplate(
        string staffName,
        string bookingCode,
        string serviceName,
        string appointmentDate,
        string appointmentTime,
        int assignedQuantity,
        string customerName,
        string customerPhone,
        string customerEmail)
    {
        return WrapHtml($@"
<h2>New service assignment</h2>
<p>Hello <strong>{staffName}</strong>,</p>
<p>You have been assigned to booking <strong>{bookingCode}</strong>.</p>
<ul>
  <li>Service: <strong>{serviceName}</strong></li>
  <li>Appointment date: <strong>{appointmentDate}</strong></li>
  <li>Appointment time: <strong>{appointmentTime}</strong></li>
  <li>Assigned quantity: <strong>{assignedQuantity}</strong></li>
  <li>Customer: <strong>{customerName}</strong></li>
  <li>Phone: <strong>{customerPhone}</strong></li>
  <li>Email: <strong>{customerEmail}</strong></li>
</ul>
<p>Please review your schedule and prepare for the appointment.</p>");
    }
}
