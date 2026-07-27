using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace SaleOrd.Licensing;

public static class MachineFingerprint
{
    // MachineGuid (HKLM\SOFTWARE\Microsoft\Cryptography) is set once at Windows
    // install/imaging time and stays stable across reboots and NIC/VPN changes,
    // unlike a MAC-address-based fingerprint.
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
