using Dapper;
using Microsoft.Data.SqlClient;
using SaleOrd.SyncAgent.Models;

namespace SaleOrd.SyncAgent.Services;

// Talks to the app's SQL Server directly via Dapper — no EF Core, no
// reference back to the web app project. Whether that server is on this
// same machine or a remote one is just a connection-string detail; the
// agent doesn't care either way. Same pattern SyncMast already uses for its
// "local agent + separately-hosted DB" shape. Table/column names below match
// the web app's EF Core defaults exactly (DbSet name = table name, property
// name = column name — no naming-convention transform is configured in
// AppDbContext), so this stays a plain mirror of that schema.
//
// Every method opens its connection explicitly (await conn.OpenAsync())
// rather than leaving it closed for Dapper to auto-open. Dapper auto-opens
// AND auto-closes a connection around each individual Query/Execute call if
// it was closed when passed in — fine for a single call, but the second
// Query on the same (now re-closed) connection re-opens it again only for
// that call, and BeginTransaction() requires an already-OPEN connection.
// Every Upsert* method here does one or more QueryAsync calls followed by
// BeginTransaction() on the same connection object, so without an explicit
// upfront OpenAsync, BeginTransaction always threw
// "InvalidOperationException: Invalid operation. The connection is closed."
public class SqlDataService
{
    private static string Normalize(string? v) => (v ?? "").Trim();

    public async Task<(bool Ok, string Message)> TestConnectionAsync(string connString)
    {
        try
        {
            using var conn = new SqlConnection(connString);
            await conn.OpenAsync();
            await conn.ExecuteScalarAsync<int>("SELECT 1");
            return (true, "Connected");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<List<Company>> GetActiveCompaniesAsync(string connString)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<Company>(
            "SELECT CompanyId, CompanyName, TallyIp, TallyPort, TallyCompanyName, IsActive, LastMasterSyncAt, LastVoucherAlterId " +
            "FROM Companies WHERE IsActive = 1");
        return rows.ToList();
    }

    public async Task UpdateLastVoucherAlterIdAsync(string connString, int companyId, long alterId)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE Companies SET LastVoucherAlterId = @AlterId WHERE CompanyId = @CompanyId",
            new { AlterId = alterId, CompanyId = companyId });
    }

    public async Task<Dictionary<string, string>> GetAppSettingsAsync(string connString)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<(string Key, string Value)>("SELECT [Key], [Value] FROM AppSettings");
        return rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);
    }

    public async Task MarkCompanySyncedAsync(string connString, int companyId)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE Companies SET LastMasterSyncAt = @Now WHERE CompanyId = @CompanyId",
            new { Now = DateTime.Now, CompanyId = companyId });
    }

    public async Task InsertSyncLogAsync(string connString, int companyId, string syncType, bool success, string? message)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "INSERT INTO SyncLogs (CompanyId, SyncType, IsSuccess, Message, SyncedAt) " +
            "VALUES (@CompanyId, @SyncType, @IsSuccess, @Message, @SyncedAt)",
            new { CompanyId = companyId, SyncType = syncType, IsSuccess = success, Message = message, SyncedAt = DateTime.Now });
    }

    // ── Master sync upserts ─────────────────────────────────────────────────

    public async Task<(int Added, int Updated)> UpsertLedgersAsync(string connString, int companyId, List<Ledger> incoming)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        var existing = (await conn.QueryAsync<(int LedgerId, string LedgerName)>(
                "SELECT LedgerId, LedgerName FROM Ledgers WHERE CompanyId = @CompanyId", new { CompanyId = companyId }))
            .GroupBy(l => Normalize(l.LedgerName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().LedgerId, StringComparer.OrdinalIgnoreCase);

        // Collapse same-name entries within this batch to the last one before
        // upserting — without this, a name appearing twice in Tally's export
        // both miss the (unchanged) `existing` lookup on their first pass,
        // so the second occurrence inserts a duplicate row instead of
        // updating the one the first occurrence just created. That orphaned
        // duplicate then never gets touched again (existing.Last() picks
        // only one of the two rows on every future sync).
        var dedup = incoming
            .Where(x => !string.IsNullOrWhiteSpace(x.LedgerName))
            .GroupBy(x => Normalize(x.LedgerName), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last());

        int added = 0, updated = 0;
        using var tx = conn.BeginTransaction();
        foreach (var l in dedup)
        {
            var name = Normalize(l.LedgerName);
            if (existing.TryGetValue(name, out var id))
            {
                await conn.ExecuteAsync(@"
                    UPDATE Ledgers SET Parent=@Parent, Address=@Address, State=@State, MobileNo=@MobileNo,
                        Email=@Email, LedgerFax=@LedgerFax, GSTNo=@GSTNo, TaxType=@TaxType,
                        IncomeTaxNo=@IncomeTaxNo, VATTINNo=@VATTINNo, CreditLimit=@CreditLimit, CreditPeriod=@CreditPeriod,
                        OpeningBalance=@OpeningBalance, ClosingBalance=@ClosingBalance,
                        GUID=@GUID, AlterId=@AlterId, LastSyncedAt=@LastSyncedAt
                    WHERE LedgerId=@LedgerId",
                    new { l.Parent, l.Address, l.State, l.MobileNo, l.Email, l.LedgerFax, l.GSTNo, l.TaxType,
                          l.IncomeTaxNo, l.VATTINNo, l.CreditLimit, l.CreditPeriod, l.OpeningBalance, l.ClosingBalance,
                          l.GUID, l.AlterId, l.LastSyncedAt, LedgerId = id }, tx);
                updated++;
            }
            else
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO Ledgers (LedgerName, Parent, Address, State, MobileNo, Email, LedgerFax,
                        GSTNo, TaxType, IncomeTaxNo, VATTINNo, CreditLimit, CreditPeriod, OpeningBalance, ClosingBalance,
                        GUID, AlterId, CompanyId, LastSyncedAt)
                    VALUES (@LedgerName, @Parent, @Address, @State, @MobileNo, @Email, @LedgerFax,
                        @GSTNo, @TaxType, @IncomeTaxNo, @VATTINNo, @CreditLimit, @CreditPeriod, @OpeningBalance, @ClosingBalance,
                        @GUID, @AlterId, @CompanyId, @LastSyncedAt)",
                    new { LedgerName = name, l.Parent, l.Address, l.State, l.MobileNo, l.Email, l.LedgerFax,
                          l.GSTNo, l.TaxType, l.IncomeTaxNo, l.VATTINNo, l.CreditLimit, l.CreditPeriod, l.OpeningBalance, l.ClosingBalance,
                          l.GUID, l.AlterId, CompanyId = companyId, l.LastSyncedAt }, tx);
                added++;
            }
        }
        tx.Commit();
        return (added, updated);
    }

    public async Task<(int Added, int Updated)> UpsertStockItemsAsync(string connString, int companyId, List<StockItem> incoming)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        var existing = (await conn.QueryAsync<(int StockItemId, string ItemName)>(
                "SELECT StockItemId, ItemName FROM StockItems WHERE CompanyId = @CompanyId", new { CompanyId = companyId }))
            .GroupBy(s => Normalize(s.ItemName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().StockItemId, StringComparer.OrdinalIgnoreCase);

        // See UpsertLedgersAsync for why same-batch names must be collapsed
        // before upserting.
        var dedup = incoming
            .Where(x => !string.IsNullOrWhiteSpace(x.ItemName))
            .GroupBy(x => Normalize(x.ItemName), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last());

        int added = 0, updated = 0;
        using var tx = conn.BeginTransaction();
        foreach (var s in dedup)
        {
            var name = Normalize(s.ItemName);
            if (existing.TryGetValue(name, out var id))
            {
                await conn.ExecuteAsync(@"
                    UPDATE StockItems SET Parent=@Parent, UOM=@UOM, AdditionalUnits=@AdditionalUnits,
                        RateOfDuty=@RateOfDuty, OpeningBalance=@OpeningBalance, ClosingBalance=@ClosingBalance,
                        OpeningValue=@OpeningValue, ClosingValue=@ClosingValue, IsBatchwiseOn=@IsBatchwiseOn,
                        IsCostTrackingOn=@IsCostTrackingOn, GUID=@GUID, AlterId=@AlterId, LastSyncedAt=@LastSyncedAt
                    WHERE StockItemId=@StockItemId",
                    new { s.Parent, s.UOM, s.AdditionalUnits, s.RateOfDuty, s.OpeningBalance, s.ClosingBalance,
                          s.OpeningValue, s.ClosingValue, s.IsBatchwiseOn, s.IsCostTrackingOn,
                          s.GUID, s.AlterId, s.LastSyncedAt, StockItemId = id }, tx);
                updated++;
            }
            else
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO StockItems (ItemName, Parent, UOM, AdditionalUnits, Rate, RateOfDuty,
                        OpeningBalance, ClosingBalance, OpeningValue, ClosingValue, IsBatchwiseOn, IsCostTrackingOn,
                        GUID, AlterId, CompanyId, LastSyncedAt)
                    VALUES (@ItemName, @Parent, @UOM, @AdditionalUnits, 0, @RateOfDuty,
                        @OpeningBalance, @ClosingBalance, @OpeningValue, @ClosingValue, @IsBatchwiseOn, @IsCostTrackingOn,
                        @GUID, @AlterId, @CompanyId, @LastSyncedAt)",
                    new { ItemName = name, s.Parent, s.UOM, s.AdditionalUnits, s.RateOfDuty,
                          s.OpeningBalance, s.ClosingBalance, s.OpeningValue, s.ClosingValue, s.IsBatchwiseOn, s.IsCostTrackingOn,
                          s.GUID, s.AlterId, CompanyId = companyId, s.LastSyncedAt }, tx);
                added++;
            }
        }
        tx.Commit();
        return (added, updated);
    }

    public async Task<(int Added, int Updated)> UpsertGodownsAsync(string connString, int companyId, List<Godown> incoming)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        var existing = (await conn.QueryAsync<(int GodownId, string GodownName)>(
                "SELECT GodownId, GodownName FROM Godowns WHERE CompanyId = @CompanyId", new { CompanyId = companyId }))
            .GroupBy(g => Normalize(g.GodownName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().GodownId, StringComparer.OrdinalIgnoreCase);

        // See UpsertLedgersAsync for why same-batch names must be collapsed
        // before upserting.
        var dedup = incoming
            .Where(x => !string.IsNullOrWhiteSpace(x.GodownName))
            .GroupBy(x => Normalize(x.GodownName), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last());

        int added = 0, updated = 0;
        using var tx = conn.BeginTransaction();
        foreach (var g in dedup)
        {
            var name = Normalize(g.GodownName);
            if (existing.TryGetValue(name, out var id))
            {
                await conn.ExecuteAsync(@"
                    UPDATE Godowns SET Parent=@Parent, Address=@Address, City=@City, State=@State,
                        PinCode=@PinCode, IsBatchwiseOn=@IsBatchwiseOn, GUID=@GUID, AlterId=@AlterId, LastSyncedAt=@LastSyncedAt
                    WHERE GodownId=@GodownId",
                    new { g.Parent, g.Address, g.City, g.State, g.PinCode, g.IsBatchwiseOn,
                          g.GUID, g.AlterId, g.LastSyncedAt, GodownId = id }, tx);
                updated++;
            }
            else
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO Godowns (GodownName, Parent, Address, City, State, PinCode, IsBatchwiseOn,
                        GUID, AlterId, CompanyId, LastSyncedAt)
                    VALUES (@GodownName, @Parent, @Address, @City, @State, @PinCode, @IsBatchwiseOn,
                        @GUID, @AlterId, @CompanyId, @LastSyncedAt)",
                    new { GodownName = name, g.Parent, g.Address, g.City, g.State, g.PinCode, g.IsBatchwiseOn,
                          g.GUID, g.AlterId, CompanyId = companyId, g.LastSyncedAt }, tx);
                added++;
            }
        }
        tx.Commit();
        return (added, updated);
    }

    // Replace-by-voucher upsert for VoucherInventoryEntry — a re-synced
    // voucher (altered in Tally, or reprocessed because a historical batch
    // overlapped) gets its old rows dropped and fresh ones inserted, rather
    // than trying to line-match and update individual inventory rows. See
    // the web app's VoucherInventorySyncService for the fuller design notes.
    public async Task<(int Added, int Updated)> UpsertVoucherInventoryAsync(
        string connString, int companyId, List<TallyService.VoucherInventoryRecord> records)
    {
        if (records.Count == 0) return (0, 0);
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();

        var ledgerIds = (await conn.QueryAsync<(int LedgerId, string LedgerName)>(
                "SELECT LedgerId, LedgerName FROM Ledgers WHERE CompanyId = @CompanyId", new { CompanyId = companyId }))
            .GroupBy(l => Normalize(l.LedgerName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().LedgerId, StringComparer.OrdinalIgnoreCase);

        var itemIds = (await conn.QueryAsync<(int StockItemId, string ItemName)>(
                "SELECT StockItemId, ItemName FROM StockItems WHERE CompanyId = @CompanyId", new { CompanyId = companyId }))
            .GroupBy(s => Normalize(s.ItemName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().StockItemId, StringComparer.OrdinalIgnoreCase);

        int added = 0, updated = 0;
        var now = DateTime.Now;
        using var tx = conn.BeginTransaction();

        foreach (var group in records.GroupBy(r => r.GUID))
        {
            var existingCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM VoucherInventoryEntries WHERE CompanyId = @CompanyId AND VoucherGUID = @Guid",
                new { CompanyId = companyId, Guid = group.Key }, tx);

            if (existingCount > 0)
            {
                await conn.ExecuteAsync(
                    "DELETE FROM VoucherInventoryEntries WHERE CompanyId = @CompanyId AND VoucherGUID = @Guid",
                    new { CompanyId = companyId, Guid = group.Key }, tx);
                updated++;
            }
            else
            {
                added++;
            }

            foreach (var rec in group)
            {
                ledgerIds.TryGetValue(Normalize(rec.PartyLedgerName), out var ledgerId);
                itemIds.TryGetValue(Normalize(rec.StockItemName), out var stockItemId);

                await conn.ExecuteAsync(@"
                    INSERT INTO VoucherInventoryEntries
                        (CompanyId, VoucherGUID, VoucherNumber, VoucherTypeName, VoucherDate, AlterId,
                         PartyLedgerName, LedgerId, StockItemName, StockItemId,
                         ActualQty, BilledQty, Rate, Amount, Discount, GodownName, LastSyncedAt)
                    VALUES
                        (@CompanyId, @VoucherGUID, @VoucherNumber, @VoucherTypeName, @VoucherDate, @AlterId,
                         @PartyLedgerName, @LedgerId, @StockItemName, @StockItemId,
                         @ActualQty, @BilledQty, @Rate, @Amount, @Discount, @GodownName, @Now)",
                    new
                    {
                        CompanyId = companyId,
                        VoucherGUID = rec.GUID,
                        rec.VoucherNumber,
                        rec.VoucherTypeName,
                        rec.VoucherDate,
                        rec.AlterId,
                        rec.PartyLedgerName,
                        LedgerId = ledgerId == 0 ? (int?)null : ledgerId,
                        rec.StockItemName,
                        StockItemId = stockItemId == 0 ? (int?)null : stockItemId,
                        rec.ActualQty,
                        rec.BilledQty,
                        rec.Rate,
                        rec.Amount,
                        rec.Discount,
                        rec.GodownName,
                        Now = now
                    }, tx);
            }
        }

        tx.Commit();
        return (added, updated);
    }

    // ── Order push / invoice check ──────────────────────────────────────────

    public async Task<List<SaleOrder>> GetPendingOrdersAsync(string connString, int companyId)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();

        var orders = (await conn.QueryAsync<SaleOrder>(
            "SELECT * FROM SaleOrders WHERE CompanyId = @CompanyId AND Status = 1", // 1 = Pending
            new { CompanyId = companyId })).ToList();
        if (orders.Count == 0) return orders;

        var orderIds = orders.Select(o => o.SaleOrderId).ToList();
        var items = (await conn.QueryAsync<SaleOrderItem>(
            "SELECT * FROM SaleOrderItems WHERE SaleOrderId IN @Ids", new { Ids = orderIds })).ToList();

        var ledgerIds = orders.Select(o => o.LedgerId).Distinct().ToList();
        var ledgers = (await conn.QueryAsync<Ledger>(
            "SELECT * FROM Ledgers WHERE LedgerId IN @Ids", new { Ids = ledgerIds }))
            .ToDictionary(l => l.LedgerId);

        foreach (var o in orders)
        {
            o.Items = items.Where(i => i.SaleOrderId == o.SaleOrderId).ToList();
            if (ledgers.TryGetValue(o.LedgerId, out var ledger)) o.Ledger = ledger;
        }
        return orders;
    }

    public async Task UpdateOrderPushResultAsync(string connString, int saleOrderId, bool success, string? tallyVoucherNo, string? message)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            UPDATE SaleOrders SET Status=@Status, SyncedAt=@Now, TallyVoucherNo=@VoucherNo, SyncError=@Error
            WHERE SaleOrderId=@Id",
            new
            {
                Status = success ? 2 : 3, // 2 = Synced, 3 = Error
                Now = DateTime.Now,
                VoucherNo = success ? tallyVoucherNo : null,
                Error = success ? null : message,
                Id = saleOrderId
            });
    }

    public async Task<List<SaleOrder>> GetUninvoicedSyncedOrdersAsync(string connString, int companyId, int take = 50)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<SaleOrder>(
            "SELECT TOP (@Take) * FROM SaleOrders WHERE CompanyId = @CompanyId AND Status = 2 AND IsInvoiced = 0 " +
            "ORDER BY SaleOrderId",
            new { Take = take, CompanyId = companyId });
        return rows.ToList();
    }

    public async Task UpdateOrderInvoiceStatusAsync(string connString, int saleOrderId, string? invoiceNo, DateTime? invoiceDate)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE SaleOrders SET IsInvoiced=1, TallyInvoiceNo=@InvNo, TallyInvoiceDate=@InvDate WHERE SaleOrderId=@Id",
            new { InvNo = invoiceNo, InvDate = invoiceDate, Id = saleOrderId });
    }
}
