using System;
using System.Security.Cryptography;
using System.Text;

namespace SaleOrd.Licensing;

// Encrypts the license key at rest (in the AppSettings table) so it isn't
// plainly readable via a DB browse. DPAPI LocalMachine scope — the ciphertext
// only decrypts on this machine, which fits since the key is tied to this one
// install anyway. This is at-rest obscurity, not the real enforcement — the
// portal's MaxActivations check is what actually stops reuse elsewhere.
public static class LicenseKeyProtector
{
    private const string Prefix = "ENC:";

    public static string Protect(string plainKey)
    {
        if (string.IsNullOrWhiteSpace(plainKey)) return plainKey;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(plainKey);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.LocalMachine);
            return Prefix + Convert.ToBase64String(encrypted);
        }
        catch
        {
            // DPAPI can fail under certain locked-down service-account contexts.
            // Saving the license is the load-bearing action — never let this
            // at-rest nicety block it. Fall back to plain text.
            return plainKey;
        }
    }

    public static string Unprotect(string? storedValue)
    {
        if (string.IsNullOrWhiteSpace(storedValue)) return string.Empty;
        if (!storedValue.StartsWith(Prefix, StringComparison.Ordinal))
            return storedValue; // legacy/plain-text value — pass through unchanged

        try
        {
            var bytes = Convert.FromBase64String(storedValue.Substring(Prefix.Length));
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            // Corrupt value, or the DB was copied from a different machine
            // (DPAPI LocalMachine blobs don't travel) — treat as absent.
            return string.Empty;
        }
    }

    // For display only: never put the real key in HTML.
    public static string Mask(string plainKey)
    {
        if (string.IsNullOrWhiteSpace(plainKey)) return string.Empty;
        if (plainKey.Length <= 8) return new string('•', plainKey.Length);
        return plainKey.Substring(0, 4) + "-••••-••••-" + plainKey.Substring(plainKey.Length - 4);
    }
}
