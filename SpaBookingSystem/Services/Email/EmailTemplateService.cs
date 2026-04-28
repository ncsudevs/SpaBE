namespace SpaBookingSystem.Api.Services.Email;

public static class EmailTemplateService
{
    public static string BuildRegisterTemplate(string fullName, string code)
    {
        return $@"
<h2>Welcome to SuSpa</h2>
<p>Hello <strong>{fullName}</strong>,</p>
<p>Your account has been created successfully.</p>
<p>Your registration code is: <strong>{code}</strong></p>
<p>Please keep this code for support or future verification.</p>";
    }

    public static string BuildPaymentRequestTemplate(string fullName, string bookingCode, string paymentCode, string method, decimal amount, string content)
    {
        return $@"
<h2>Payment request received</h2>
<p>Hello <strong>{fullName}</strong>,</p>
<p>We have received your payment request for booking <strong>{bookingCode}</strong>.</p>
<ul>
  <li>Payment code: <strong>{paymentCode}</strong></li>
  <li>Method: <strong>{method}</strong></li>
  <li>Amount: <strong>{amount:N0}</strong></li>
  <li>Transfer content: <strong>{content}</strong></li>
</ul>
<p>Your order is waiting for admin confirmation.</p>";
    }

    public static string BuildBankTransferInstructionTemplate(string fullName, string bookingCode, string paymentCode, string providerName, string accountNumber, string accountName, decimal amount, string content, string instruction)
    {
        return $@"
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
<p>{instruction}</p>";
    }

    public static string BuildCashierPaymentSubmittedTemplate(string bookingCode, string paymentCode, string customerName, decimal amount, string content)
    {
        return $@"
<h2>Customer submitted bank transfer</h2>
<p>A customer has confirmed a bank transfer and is waiting for cashier review.</p>
<ul>
  <li>Booking code: <strong>{bookingCode}</strong></li>
  <li>Payment code: <strong>{paymentCode}</strong></li>
  <li>Customer: <strong>{customerName}</strong></li>
  <li>Amount: <strong>{amount:N0}</strong></li>
  <li>Transfer content: <strong>{content}</strong></li>
</ul>
<p>Please review the incoming transfer and update the payment status.</p>";
    }

    public static string BuildBankTransferSubmittedTemplate(string fullName, string bookingCode, string paymentCode)
    {
        return $@"
<h2>Transfer confirmation received</h2>
<p>Hello <strong>{fullName}</strong>,</p>
<p>We have received your transfer confirmation for booking <strong>{bookingCode}</strong>.</p>
<p>Payment code: <strong>{paymentCode}</strong></p>
<p>The cashier will review the transfer and update your booking once it is verified.</p>";
    }

    public static string BuildPaymentConfirmedTemplate(string fullName, string bookingCode, string paymentCode)
    {
        return $@"
<h2>Payment confirmed</h2>
<p>Hello <strong>{fullName}</strong>,</p>
<p>Your payment for booking <strong>{bookingCode}</strong> has been confirmed.</p>
<p>Payment code: <strong>{paymentCode}</strong></p>
<p>Your booking status is now <strong>CONFIRMED</strong>.</p>";
    }

    public static string BuildPaymentRejectedTemplate(string fullName, string bookingCode, string paymentCode)
    {
        return $@"
<h2>Payment rejected</h2>
<p>Hello <strong>{fullName}</strong>,</p>
<p>Your payment for booking <strong>{bookingCode}</strong> could not be confirmed.</p>
<p>Payment code: <strong>{paymentCode}</strong></p>
<p>Please review your transfer and submit payment again.</p>";
    }
}
