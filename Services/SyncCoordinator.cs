namespace SaleOrd.Services;

// Shared across MasterSyncJob, OrderPushJob, and the manual Sync button —
// all three ultimately hit the same Tally instance, so only one runs at a
// time. If a trigger fires while another sync is still in flight, that
// trigger is skipped outright rather than queued or run in parallel.
public class SyncCoordinator
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool TryStart() => _lock.Wait(0);

    public void Finish() => _lock.Release();
}
