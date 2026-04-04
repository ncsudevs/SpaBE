namespace SpaBookingSystem.Api.Services.Momo;

public interface IMomoService
{
    Task<MomoCreatePaymentResponse> CreatePaymentAsync(MomoCreatePaymentRequest request, CancellationToken cancellationToken = default);
    string CreateCreatePaymentSignature(MomoCreatePaymentRequest request);
    bool IsValidIpnSignature(MomoIpnRequest request);
}
