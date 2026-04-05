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
            errorMessage = "Phone number is required.";
            return false;
        }

        var cleaned = rawPhone.Trim();
        // Remove common separators to be more forgiving.
        cleaned = cleaned.Replace(" ", string.Empty)
                         .Replace("-", string.Empty)
                         .Replace(".", string.Empty)
                         .Replace("(", string.Empty)
                         .Replace(")", string.Empty);

        // Handle leading 00 as international prefix.
        if (cleaned.StartsWith("00"))
        {
            cleaned = "+" + cleaned[2..];
        }

        var normalizedRegion = string.IsNullOrWhiteSpace(region)
            ? "VN"
            : region.Trim().ToUpper();

        var phoneUtil = PhoneNumberUtil.GetInstance();

        bool TryParse(string phone, string? regionCode, out string formatted, out string err)
        {
            formatted = string.Empty;
            err = string.Empty;
            try
            {
                var parsed = phoneUtil.Parse(phone, regionCode);

                if (!phoneUtil.IsValidNumber(parsed))
                {
                    err = "Invalid phone number";
                    return false;
                }

                // If region was supplied, ensure it matches when possible.
                if (!string.IsNullOrWhiteSpace(regionCode) &&
                    !phoneUtil.IsValidNumberForRegion(parsed, regionCode))
                {
                    err = $"Invalid phone number for region {regionCode}";
                    return false;
                }

                formatted = phoneUtil.Format(parsed, PhoneNumberFormat.E164);
                return true;
            }
            catch (NumberParseException ex)
            {
                err = ex.Message;
                return false;
            }
        }

        // Try with region hint first
        if (TryParse(cleaned, normalizedRegion, out var e164, out var err1))
        {
            normalizedPhone = e164;
            return true;
        }

        // Fallback: try parsing as international number without region
        if (TryParse(cleaned, null, out var e164Intl, out var err2))
        {
            normalizedPhone = e164Intl;
            return true;
        }

        var friendlyRegion = normalizedRegion == "VN" ? "Vietnam (+84)" : normalizedRegion;
        var friendly = string.IsNullOrWhiteSpace(err1) ? err2 : err1;
        errorMessage = $"Invalid phone number. Please enter a valid number for {friendlyRegion} (e.g. 0901xxxxxx) or use international format +84...";
        return false;
    }
}
