using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaleOrd.SyncAgent.Models;

namespace SaleOrd.SyncAgent.Services;

// Loads/saves the agent's local machine config. Lives under %AppData%, not
// alongside the exe, so it survives an in-place upgrade and isn't wiped by
// an installer that overwrites the program folder. The SQL password is
// DPAPI-protected (LocalMachine scope) — same pattern as the web app's
// LicenseKeyProtector — so it isn't sitting in plain text in the JSON file.
public class ConfigService
{
    private const string Prefix = "ENC:";

    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaleOrdSyncAgent");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    public static string LogFilePath => Path.Combine(ConfigDir, "agent.log");

    public AgentConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new AgentConfig();
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AgentConfig>(json) ?? new AgentConfig();
        }
        catch
        {
            return new AgentConfig();
        }
    }

    public void Save(AgentConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    public static string ProtectPassword(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(plain);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.LocalMachine);
            return Prefix + Convert.ToBase64String(encrypted);
        }
        catch
        {
            return plain;
        }
    }

    public static string UnprotectPassword(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;
        try
        {
            var bytes = Convert.FromBase64String(stored.Substring(Prefix.Length));
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string BuildConnectionString(AgentConfig cfg)
    {
        var password = UnprotectPassword(cfg.SqlPasswordProtected);
        return $"Server={cfg.SqlServer};Database={cfg.SqlDatabase};User Id={cfg.SqlUser};Password={password};" +
               $"TrustServerCertificate=True;Encrypt={(cfg.SqlEncrypt ? "True" : "False")};Connect Timeout=10;";
    }
}
