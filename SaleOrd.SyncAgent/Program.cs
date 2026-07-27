using SaleOrd.SyncAgent.Forms;

namespace SaleOrd.SyncAgent;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new FrmMain());
    }
}
