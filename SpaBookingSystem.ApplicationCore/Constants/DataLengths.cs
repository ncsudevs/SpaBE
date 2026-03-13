namespace SpaBookingSystem.ApplicationCore.Constants;

public static class DataLengths
{
    // ===== Common =====
    public const int NAME = 150;
    public const int EMAIL = 150;
    public const int USERNAME = 50;

    // ===== Text content =====
    public const int SHORT_DESCRIPTION = 255;
    public const int DESCRIPTION = 4000;
    public const int CONTENT = 8000;

    // ===== Security =====
    public const int PASSWORD_HASH = 255;
    public const int TOKEN = 500;

    // ===== Image / URL =====
    public const int IMAGE_URL = 500;

    // ===== Status / Enum stored as string =====
    public const int STATUS = 30;
    public const int ROLE_NAME = 50;
}
