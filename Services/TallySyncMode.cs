namespace SaleOrd.Services;

// Deployment-mode switch for who actually talks to Tally.
//
// "Local" (default) — this web app's own background jobs (MasterSyncJob,
// OrderPushJob) and the manual Sync button call Tally directly. Unchanged
// behavior for every existing install.
//
// "Agent" — a separate SaleOrd.SyncAgent (WinForms) handles all Tally<->SQL
// sync instead, and this app's own sync jobs disable themselves. This is
// NOT specifically about cloud vs on-prem topology — the agent and the DB
// can just as well sit on the same machine (agent's SqlServer config set to
// localhost). The switch is really about which process owns the Tally
// conversation, not where anything is physically hosted. Going forward,
// "Agent" mode is the intended default for new installs; "Local" only
// exists so already-deployed clients keep working unchanged.
//
// Either way, SyncLogs/Admin panels behave identically, since they only
// ever read rows from the DB regardless of which process wrote them.
public static class TallySyncMode
{
    public static bool IsAgentManaged(IConfiguration config) =>
        string.Equals(config["TallySync:Mode"], "Agent", StringComparison.OrdinalIgnoreCase);
}
