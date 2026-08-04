using SalesPush.SyncAgent.Forms;

namespace SalesPush.SyncAgent;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new FrmMain());
    }
}
