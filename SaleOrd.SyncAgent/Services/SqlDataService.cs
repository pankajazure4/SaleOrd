using System.Data;
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

    // DataTable/SqlBulkCopy has no concept of a C# null — needs DBNull.Value
    // explicitly, unlike Dapper's parameter objects which handled this
    // automatically.
    private static object Db(object? v) => v ?? DBNull.Value;

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

    // ── Manually-created party push (web app Party Master approval) ────────
    //
    // The web app's Admin > Pending Parties screen only ever flips
    // ApprovalStatus locally — it has no direct route to the client's Tally
    // instance (TallySync:Mode=Agent), so it never pushes to Tally itself.
    // This Agent is the only thing that talks to Tally, so it's responsible
    // for finding every Approved + manually-created ledger that hasn't been
    // pushed yet (TallyPushedAt IS NULL) and creating it as a real Tally
    // ledger master. 1 = Approved, matching LedgerApprovalStatus in the web app.
    public async Task<List<Ledger>> GetApprovedUnpushedManualPartiesAsync(string connString, int companyId)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<Ledger>(
            "SELECT LedgerId, LedgerName, Parent, Address, State, PinCode, MobileNo, Email, LedgerFax, " +
            "GSTNo, TaxType, IncomeTaxNo, VATTINNo, FSSAINo, CreditLimit, CreditPeriod, OpeningBalance, " +
            "ClosingBalance, GUID, AlterId, CompanyId, LastSyncedAt, IsManuallyCreated, ApprovalStatus, TallyPushedAt " +
            "FROM Ledgers WHERE CompanyId = @CompanyId AND IsManuallyCreated = 1 AND ApprovalStatus = 1 AND TallyPushedAt IS NULL",
            new { CompanyId = companyId });
        return rows.ToList();
    }

    public async Task MarkLedgerPushedToTallyAsync(string connString, int ledgerId)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE Ledgers SET TallyPushedAt = @Now WHERE LedgerId = @LedgerId",
            new { Now = DateTime.Now, LedgerId = ledgerId });
    }

    // ── Master sync upserts ─────────────────────────────────────────────────

    // Bulk upsert: SqlBulkCopy the whole batch into a temp staging table (one
    // round trip) then a single MERGE applies every insert/update at once.
    // This replaced one INSERT/UPDATE statement PER ROW, executed
    // sequentially — fine on a local SQL Server, but over any real network
    // latency to a remote one, thousands of individual round trips is what
    // was making this step take a full minute-plus even when nothing had
    // actually changed (observed: 2156 stock items, 0 changes, 60s).
    public async Task<(int Added, int Updated)> UpsertLedgersAsync(string connString, int companyId, List<Ledger> incoming)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        var existingNames = (await conn.QueryAsync<string>(
                "SELECT LedgerName FROM Ledgers WHERE CompanyId = @CompanyId", new { CompanyId = companyId }))
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Collapse same-name entries within this batch to the last one before
        // upserting — without this, a name appearing twice in Tally's export
        // both miss the (unchanged) `existing` lookup on their first pass,
        // so the second occurrence inserts a duplicate row instead of
        // updating the one the first occurrence just created.
        var dedup = incoming
            .Where(x => !string.IsNullOrWhiteSpace(x.LedgerName))
            .GroupBy(x => Normalize(x.LedgerName), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        if (dedup.Count == 0) return (0, 0);

        int updated = dedup.Count(l => existingNames.Contains(Normalize(l.LedgerName)));
        int added = dedup.Count - updated;

        var table = new DataTable();
        table.Columns.Add("LedgerName", typeof(string));
        table.Columns.Add("Parent", typeof(string));
        table.Columns.Add("Address", typeof(string));
        table.Columns.Add("State", typeof(string));
        table.Columns.Add("PinCode", typeof(string));
        table.Columns.Add("MobileNo", typeof(string));
        table.Columns.Add("Email", typeof(string));
        table.Columns.Add("LedgerFax", typeof(string));
        table.Columns.Add("GSTNo", typeof(string));
        table.Columns.Add("TaxType", typeof(string));
        table.Columns.Add("IncomeTaxNo", typeof(string));
        table.Columns.Add("VATTINNo", typeof(string));
        table.Columns.Add("CreditLimit", typeof(decimal));
        table.Columns.Add("CreditPeriod", typeof(string));
        table.Columns.Add("OpeningBalance", typeof(decimal));
        table.Columns.Add("ClosingBalance", typeof(decimal));
        table.Columns.Add("GUID", typeof(string));
        table.Columns.Add("AlterId", typeof(long));
        table.Columns.Add("LastSyncedAt", typeof(DateTime));

        foreach (var l in dedup)
        {
            table.Rows.Add(
                Normalize(l.LedgerName), Db(l.Parent), Db(l.Address), Db(l.State), Db(l.PinCode), Db(l.MobileNo),
                Db(l.Email), Db(l.LedgerFax), Db(l.GSTNo), Db(l.TaxType), Db(l.IncomeTaxNo), Db(l.VATTINNo),
                l.CreditLimit, Db(l.CreditPeriod), l.OpeningBalance, l.ClosingBalance, Db(l.GUID), l.AlterId, l.LastSyncedAt);
        }

        using var tx = conn.BeginTransaction();

        // Column sizes/precision here match the real Ledgers table exactly
        // (Migrations/AppDbContextModelSnapshot.cs) — LedgerName nvarchar(450)
        // to match its unique (CompanyId, LedgerName) index, everything else
        // nvarchar(max)/decimal(18,2) as EF Core defines them.
        await conn.ExecuteAsync(@"
            CREATE TABLE #LedgersStaging (
                LedgerName nvarchar(450), Parent nvarchar(max), Address nvarchar(max), State nvarchar(max),
                PinCode nvarchar(max), MobileNo nvarchar(max), Email nvarchar(max), LedgerFax nvarchar(max),
                GSTNo nvarchar(max), TaxType nvarchar(max), IncomeTaxNo nvarchar(max), VATTINNo nvarchar(max),
                CreditLimit decimal(18,2), CreditPeriod nvarchar(max), OpeningBalance decimal(18,2), ClosingBalance decimal(18,2),
                GUID nvarchar(max), AlterId bigint, LastSyncedAt datetime2)", transaction: tx);

        using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = "#LedgersStaging" })
        {
            foreach (DataColumn col in table.Columns)
                bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            await bulk.WriteToServerAsync(table);
        }

        // Default SQL Server collation is case-insensitive (CI) — matches the
        // OrdinalIgnoreCase name matching used everywhere else in this file.
        await conn.ExecuteAsync(@"
            MERGE INTO Ledgers AS target
            USING #LedgersStaging AS source
            ON target.CompanyId = @CompanyId AND target.LedgerName = source.LedgerName
            WHEN MATCHED THEN UPDATE SET
                Parent=source.Parent, Address=source.Address, State=source.State, PinCode=source.PinCode,
                MobileNo=source.MobileNo, Email=source.Email, LedgerFax=source.LedgerFax, GSTNo=source.GSTNo,
                TaxType=source.TaxType, IncomeTaxNo=source.IncomeTaxNo, VATTINNo=source.VATTINNo,
                CreditLimit=source.CreditLimit, CreditPeriod=source.CreditPeriod,
                OpeningBalance=source.OpeningBalance, ClosingBalance=source.ClosingBalance,
                GUID=source.GUID, AlterId=source.AlterId, LastSyncedAt=source.LastSyncedAt
            WHEN NOT MATCHED THEN INSERT
                (LedgerName, Parent, Address, State, PinCode, MobileNo, Email, LedgerFax, GSTNo, TaxType,
                 IncomeTaxNo, VATTINNo, CreditLimit, CreditPeriod, OpeningBalance, ClosingBalance, GUID, AlterId, CompanyId, LastSyncedAt)
            VALUES
                (source.LedgerName, source.Parent, source.Address, source.State, source.PinCode, source.MobileNo,
                 source.Email, source.LedgerFax, source.GSTNo, source.TaxType, source.IncomeTaxNo, source.VATTINNo,
                 source.CreditLimit, source.CreditPeriod, source.OpeningBalance, source.ClosingBalance, source.GUID, source.AlterId, @CompanyId, source.LastSyncedAt);
            DROP TABLE #LedgersStaging;",
            new { CompanyId = companyId }, tx);

        tx.Commit();
        return (added, updated);
    }

    // See UpsertLedgersAsync's header comment for why this is bulk-loaded via
    // a temp staging table + MERGE instead of one statement per row.
    public async Task<(int Added, int Updated)> UpsertStockItemsAsync(string connString, int companyId, List<StockItem> incoming)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        var existingNames = (await conn.QueryAsync<string>(
                "SELECT ItemName FROM StockItems WHERE CompanyId = @CompanyId", new { CompanyId = companyId }))
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // See UpsertLedgersAsync for why same-batch names must be collapsed
        // before upserting.
        var dedup = incoming
            .Where(x => !string.IsNullOrWhiteSpace(x.ItemName))
            .GroupBy(x => Normalize(x.ItemName), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        if (dedup.Count == 0) return (0, 0);

        int updated = dedup.Count(s => existingNames.Contains(Normalize(s.ItemName)));
        int added = dedup.Count - updated;

        var table = new DataTable();
        table.Columns.Add("ItemName", typeof(string));
        table.Columns.Add("Parent", typeof(string));
        table.Columns.Add("UOM", typeof(string));
        table.Columns.Add("AdditionalUnits", typeof(string));
        table.Columns.Add("RateOfDuty", typeof(decimal));
        table.Columns.Add("OpeningBalance", typeof(decimal));
        table.Columns.Add("ClosingBalance", typeof(decimal));
        table.Columns.Add("OpeningValue", typeof(decimal));
        table.Columns.Add("ClosingValue", typeof(decimal));
        table.Columns.Add("IsBatchwiseOn", typeof(bool));
        table.Columns.Add("IsCostTrackingOn", typeof(bool));
        table.Columns.Add("GUID", typeof(string));
        table.Columns.Add("AlterId", typeof(long));
        table.Columns.Add("LastSyncedAt", typeof(DateTime));

        foreach (var s in dedup)
        {
            table.Rows.Add(
                Normalize(s.ItemName), Db(s.Parent), Db(s.UOM), Db(s.AdditionalUnits), s.RateOfDuty,
                s.OpeningBalance, s.ClosingBalance, s.OpeningValue, s.ClosingValue, s.IsBatchwiseOn, s.IsCostTrackingOn,
                Db(s.GUID), s.AlterId, s.LastSyncedAt);
        }

        using var tx = conn.BeginTransaction();

        // Column sizes/precision here match the real StockItems table exactly
        // (Migrations/AppDbContextModelSnapshot.cs) — ItemName nvarchar(450)
        // to match its unique (CompanyId, ItemName) index, RateOfDuty
        // decimal(18,2) (not (9,3) — that's SaleOrderItem's tax-rate
        // precision, a different table), everything else matching EF Core's
        // own defaults.
        await conn.ExecuteAsync(@"
            CREATE TABLE #StockItemsStaging (
                ItemName nvarchar(450), Parent nvarchar(max), UOM nvarchar(max), AdditionalUnits nvarchar(max),
                RateOfDuty decimal(18,2), OpeningBalance decimal(18,3), ClosingBalance decimal(18,3),
                OpeningValue decimal(18,2), ClosingValue decimal(18,2), IsBatchwiseOn bit, IsCostTrackingOn bit,
                GUID nvarchar(max), AlterId bigint, LastSyncedAt datetime2)", transaction: tx);

        using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = "#StockItemsStaging" })
        {
            foreach (DataColumn col in table.Columns)
                bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            await bulk.WriteToServerAsync(table);
        }

        // Rate is deliberately untouched on both branches here — it's not in
        // the staging table at all — matching the original code exactly:
        // UPDATE never touched Rate, and INSERT always hardcoded 0 for it.
        // (Comment preserved from the row-by-row version; wherever Rate
        // actually gets its real value is a separate code path.)
        await conn.ExecuteAsync(@"
            MERGE INTO StockItems AS target
            USING #StockItemsStaging AS source
            ON target.CompanyId = @CompanyId AND target.ItemName = source.ItemName
            WHEN MATCHED THEN UPDATE SET
                Parent=source.Parent, UOM=source.UOM, AdditionalUnits=source.AdditionalUnits,
                RateOfDuty=source.RateOfDuty, OpeningBalance=source.OpeningBalance, ClosingBalance=source.ClosingBalance,
                OpeningValue=source.OpeningValue, ClosingValue=source.ClosingValue, IsBatchwiseOn=source.IsBatchwiseOn,
                IsCostTrackingOn=source.IsCostTrackingOn, GUID=source.GUID, AlterId=source.AlterId, LastSyncedAt=source.LastSyncedAt
            WHEN NOT MATCHED THEN INSERT
                (ItemName, Parent, UOM, AdditionalUnits, Rate, RateOfDuty, OpeningBalance, ClosingBalance,
                 OpeningValue, ClosingValue, IsBatchwiseOn, IsCostTrackingOn, GUID, AlterId, CompanyId, LastSyncedAt)
            VALUES
                (source.ItemName, source.Parent, source.UOM, source.AdditionalUnits, 0, source.RateOfDuty,
                 source.OpeningBalance, source.ClosingBalance, source.OpeningValue, source.ClosingValue,
                 source.IsBatchwiseOn, source.IsCostTrackingOn, source.GUID, source.AlterId, @CompanyId, source.LastSyncedAt);
            DROP TABLE #StockItemsStaging;",
            new { CompanyId = companyId }, tx);

        tx.Commit();
        return (added, updated);
    }

    // Full-replace: every item's slab set is dropped and reinserted from the
    // latest Tally data each sync — an item's GST history is small (a
    // handful of rate-notification dates), so this is cheap and avoids
    // reconciling individual slab rows. Must run after UpsertStockItemsAsync
    // so StockItemIds exist for any newly-added items.
    public async Task UpsertStockItemTaxSlabsAsync(
        string connString, int companyId, List<TallyService.StockItemTaxSlabRecord> taxSlabs)
    {
        if (taxSlabs.Count == 0) return;

        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();

        var itemIds = (await conn.QueryAsync<(int StockItemId, string ItemName)>(
                "SELECT StockItemId, ItemName FROM StockItems WHERE CompanyId = @CompanyId", new { CompanyId = companyId }))
            .GroupBy(s => Normalize(s.ItemName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().StockItemId, StringComparer.OrdinalIgnoreCase);

        using var tx = conn.BeginTransaction();

        foreach (var group in taxSlabs.GroupBy(t => Normalize(t.ItemName), StringComparer.OrdinalIgnoreCase))
        {
            if (!itemIds.TryGetValue(group.Key, out var stockItemId)) continue;

            await conn.ExecuteAsync(
                "DELETE FROM StockItemTaxSlabs WHERE StockItemId = @StockItemId",
                new { StockItemId = stockItemId }, tx);

            foreach (var slab in group)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO StockItemTaxSlabs
                        (StockItemId, CompanyId, ApplicableFrom, CGSTRate, SGSTRate, IGSTRate, CessRate, StateCessRate)
                    VALUES
                        (@StockItemId, @CompanyId, @ApplicableFrom, @CGSTRate, @SGSTRate, @IGSTRate, @CessRate, @StateCessRate)",
                    new
                    {
                        StockItemId = stockItemId,
                        CompanyId = companyId,
                        slab.ApplicableFrom,
                        slab.CGSTRate,
                        slab.SGSTRate,
                        slab.IGSTRate,
                        slab.CessRate,
                        slab.StateCessRate
                    }, tx);
            }
        }

        tx.Commit();
    }

    // See UpsertLedgersAsync's header comment for why this is bulk-loaded via
    // a temp staging table + MERGE instead of one statement per row.
    public async Task<(int Added, int Updated)> UpsertGodownsAsync(string connString, int companyId, List<Godown> incoming)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        var existingNames = (await conn.QueryAsync<string>(
                "SELECT GodownName FROM Godowns WHERE CompanyId = @CompanyId", new { CompanyId = companyId }))
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // See UpsertLedgersAsync for why same-batch names must be collapsed
        // before upserting.
        var dedup = incoming
            .Where(x => !string.IsNullOrWhiteSpace(x.GodownName))
            .GroupBy(x => Normalize(x.GodownName), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        if (dedup.Count == 0) return (0, 0);

        int updated = dedup.Count(g => existingNames.Contains(Normalize(g.GodownName)));
        int added = dedup.Count - updated;

        var table = new DataTable();
        table.Columns.Add("GodownName", typeof(string));
        table.Columns.Add("Parent", typeof(string));
        table.Columns.Add("Address", typeof(string));
        table.Columns.Add("City", typeof(string));
        table.Columns.Add("State", typeof(string));
        table.Columns.Add("PinCode", typeof(string));
        table.Columns.Add("IsBatchwiseOn", typeof(bool));
        table.Columns.Add("GUID", typeof(string));
        table.Columns.Add("AlterId", typeof(long));
        table.Columns.Add("LastSyncedAt", typeof(DateTime));

        foreach (var g in dedup)
        {
            table.Rows.Add(
                Normalize(g.GodownName), Db(g.Parent), Db(g.Address), Db(g.City), Db(g.State), Db(g.PinCode),
                g.IsBatchwiseOn, Db(g.GUID), g.AlterId, g.LastSyncedAt);
        }

        using var tx = conn.BeginTransaction();

        // Matches the real Godowns table exactly (Migrations/AppDbContextModelSnapshot.cs)
        // — nvarchar(max) throughout, no unique-index size constraint on GodownName.
        await conn.ExecuteAsync(@"
            CREATE TABLE #GodownsStaging (
                GodownName nvarchar(max), Parent nvarchar(max), Address nvarchar(max), City nvarchar(max),
                State nvarchar(max), PinCode nvarchar(max), IsBatchwiseOn bit, GUID nvarchar(max),
                AlterId bigint, LastSyncedAt datetime2)", transaction: tx);

        using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = "#GodownsStaging" })
        {
            foreach (DataColumn col in table.Columns)
                bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            await bulk.WriteToServerAsync(table);
        }

        await conn.ExecuteAsync(@"
            MERGE INTO Godowns AS target
            USING #GodownsStaging AS source
            ON target.CompanyId = @CompanyId AND target.GodownName = source.GodownName
            WHEN MATCHED THEN UPDATE SET
                Parent=source.Parent, Address=source.Address, City=source.City, State=source.State,
                PinCode=source.PinCode, IsBatchwiseOn=source.IsBatchwiseOn, GUID=source.GUID,
                AlterId=source.AlterId, LastSyncedAt=source.LastSyncedAt
            WHEN NOT MATCHED THEN INSERT
                (GodownName, Parent, Address, City, State, PinCode, IsBatchwiseOn, GUID, AlterId, CompanyId, LastSyncedAt)
            VALUES
                (source.GodownName, source.Parent, source.Address, source.City, source.State, source.PinCode,
                 source.IsBatchwiseOn, source.GUID, source.AlterId, @CompanyId, source.LastSyncedAt);
            DROP TABLE #GodownsStaging;",
            new { CompanyId = companyId }, tx);

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

    // No TOP/limit here on purpose — this used to be "TOP 50, ORDER BY
    // SaleOrderId" with no rotation, which meant every cycle re-checked the
    // SAME oldest 50 uninvoiced orders. If any of those never actually match
    // (old test data, a genuinely un-invoiced order that's just sitting),
    // the queue never advances — confirmed on a real client where nothing
    // past a specific date ever got checked at all, because the oldest 50
    // permanently occupied every cycle's whole batch. Fetching everyone
    // doesn't widen the Tally query's date range either — that's already
    // anchored to the oldest uninvoiced order's date regardless of how many
    // orders are in this list (see RunOrderCycleAsync's fromDate), so this
    // is free: same one Tally round-trip, just matched against the full set
    // instead of an arbitrary 50-row slice of it.
    public async Task<List<SaleOrder>> GetUninvoicedSyncedOrdersAsync(string connString, int companyId)
    {
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<SaleOrder>(
            "SELECT * FROM SaleOrders WHERE CompanyId = @CompanyId AND Status = 2 AND IsInvoiced = 0 " +
            "ORDER BY SaleOrderId",
            new { CompanyId = companyId });
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
