namespace SaleOrd.SyncAgent.Licensing;

// For display only: never show the real key once it's been saved.
public static class LicenseKeyMask
{
    public static string Mask(string plainKey)
    {
        if (string.IsNullOrWhiteSpace(plainKey)) return string.Empty;
        if (plainKey.Length <= 8) return new string('•', plainKey.Length);
        return plainKey.Substring(0, 4) + "-••••-••••-" + plainKey.Substring(plainKey.Length - 4);
    }
}
