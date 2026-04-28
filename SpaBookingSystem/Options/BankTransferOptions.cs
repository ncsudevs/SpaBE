namespace SpaBookingSystem.Api.Options;

public class BankTransferOptions
{
    public const string SectionName = "BankTransfer";

    public string ProviderName { get; set; } = "SuSpa Bank Transfer";
    public string BankName { get; set; } = "VCB";
    public string AccountNumber { get; set; } = "0123456789";
    public string AccountName { get; set; } = "CONG TY SUSPA";
    public string Instruction { get; set; } = "Transfer the exact amount and then press Confirm transfer so the cashier can review it.";
}
