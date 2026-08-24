namespace SaleOrd.Services;

// Shared across MasterSyncJob, OrderPushJob, and the manual Sync button —
// all three ultimately hit the same Tally instance, so only one runs at a
// time. TryStart() (skip-if-busy) is fine for master sync — if it loses the
// race, the next scheduled tick a short while later just tries again, so
// nothing meaningful is lost. Order push must not use the same non-blocking
// form: on SaleOrd.SyncAgent (which had this identical pattern) that meant a
// scheduled order-push cycle could be dropped outright rather than merely
// delayed, so orders could sit unsynced for a full extra interval — use
// StartAsync() there instead, which waits for master sync to finish rather
// than skipping. See SaleOrd.SyncAgent/Services/SyncOrchestrator.cs for the
// mirrored fix (this web-app path is currently dormant under
// TallySync:Mode=Agent, but kept in sync in case Local mode is ever used
// again).
public class SyncCoordinator
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool TryStart() => _lock.Wait(0);

    public Task StartAsync() => _lock.WaitAsync();

    public void Finish() => _lock.Release();
}
