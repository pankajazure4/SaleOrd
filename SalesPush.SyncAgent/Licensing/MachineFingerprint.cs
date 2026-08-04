using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace SalesPush.SyncAgent.Licensing;

// Ported from SaleOrd's Licensing/MachineFingerprint.cs — identical logic,
// kept in sync deliberately so a machine fingerprint computed by either
// product for the same PC is byte-identical (not that they share a license,
// just that the algorithm shouldn't silently drift between our own products).
public static class MachineFingerprint
{
    public static string GetUniqueMachineId()
    {
        var machineGuid = GetMachineGuid();
        if (machineGuid != "NOGUID")
            return ComputeSha256(machineGuid);

        try
        {
            return ComputeSha256(Environment.MachineName);
        }
        catch
        {
            return ComputeSha256("UNKNOWN-MACHINE");
        }
    }

    private static string GetMachineGuid()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid")?.ToString() ?? "NOGUID";
        }
        catch
        {
            return "NOGUID";
        }
    }

    private static string ComputeSha256(string rawData)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));
        var sb = new StringBuilder();
        foreach (var b in bytes)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
