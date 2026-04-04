using PhoneNumbers;

namespace SpaBookingSystem.Api.Helpers;

public static class PhoneHelper
{
    public static bool TryNormalizePhone(
        string? rawPhone,
        string? region,
        out string normalizedPhone,
        out string errorMessage)
    {
        normalizedPhone = string.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(rawPhone))
        {
            errorMessage = "Phone number is required";
            return false;
        }

        var normalizedRegion = string.IsNullOrWhiteSpace(region)
            ? "VN"
            : region.Trim().ToUpper();

        try
        {
            var phoneUtil = PhoneNumberUtil.GetInstance();
            var parsedPhone = phoneUtil.Parse(rawPhone.Trim(), normalizedRegion);

            if (!phoneUtil.IsValidNumberForRegion(parsedPhone, normalizedRegion))
            {
                errorMessage = $"Invalid phone number for region {normalizedRegion}";
                return false;
            }

            normalizedPhone = phoneUtil.Format(parsedPhone, PhoneNumberFormat.E164);
            return true;
        }
        catch
        {
            errorMessage = "Invalid phone number format";
            return false;
        }
    }
}