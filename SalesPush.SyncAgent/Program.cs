using SalesPush.SyncAgent.Forms;
using SalesPush.SyncAgent.Services;

namespace SalesPush.SyncAgent;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var configSvc = new ConfigService();
        var config = configSvc.Load();
        var licenseSvc = new LicenseService(configSvc);

        // Gate app startup on a valid license — FrmLicense.ShowDialog() runs
        // its own modal message loop, which is fine to call before
        // Application.Run() as long as we're on an STA thread and
        // ApplicationConfiguration.Initialize() already ran (both true here).
        using (var frmLicense = new FrmLicense(licenseSvc, config))
        {
            if (frmLicense.ShowDialog() != DialogResult.OK)
                return; // not activated — exit without starting the agent
        }

        Application.Run(new FrmMain());
    }
}
