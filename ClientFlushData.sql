-- ============================================================================
-- SaleOrd — full data flush, keeping ONLY:
--   Companies, all Identity/user tables (AspNetUsers etc.), Roles +
--   RolePermissions, UserCompanies (user<->company access mapping — kept
--   since both endpoints, Users and Companies, are being kept), and
--   AppSettings (configurations).
-- Everything else — masters synced from Tally, godown-wise stock balances,
-- Sale Orders, sync logs, rate history, signup requests, user activity —
-- is deleted.
--
-- Wrapped in one transaction with XACT_ABORT so a failure anywhere rolls
-- back everything instead of leaving a half-flushed DB.
-- ============================================================================
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Children first, in FK-dependency order (most FKs here are NO ACTION, so
-- SQL Server blocks deleting a parent while a child row still points at it).
DELETE FROM SaleOrderItems;
DELETE FROM StockItemGodownBalances;
DELETE FROM StockItemTaxSlabs;
DELETE FROM VoucherInventoryEntries;
DELETE FROM LastSaleRates;
DELETE FROM SaleOrders;
DELETE FROM SyncLogs;
DELETE FROM UserActivities;
DELETE FROM SignupRequests;
DELETE FROM Ledgers;
DELETE FROM StockItems;
DELETE FROM Godowns;

-- Reset identity seeds so new rows start clean from 1 again (safe to
-- remove these lines if you don't care about IDs restarting).
DBCC CHECKIDENT ('SaleOrderItems', RESEED, 0);
DBCC CHECKIDENT ('StockItemGodownBalances', RESEED, 0);
DBCC CHECKIDENT ('StockItemTaxSlabs', RESEED, 0);
DBCC CHECKIDENT ('VoucherInventoryEntries', RESEED, 0);
DBCC CHECKIDENT ('LastSaleRates', RESEED, 0);
DBCC CHECKIDENT ('SaleOrders', RESEED, 0);
DBCC CHECKIDENT ('SyncLogs', RESEED, 0);
DBCC CHECKIDENT ('UserActivities', RESEED, 0);
DBCC CHECKIDENT ('SignupRequests', RESEED, 0);
DBCC CHECKIDENT ('Ledgers', RESEED, 0);
DBCC CHECKIDENT ('StockItems', RESEED, 0);
DBCC CHECKIDENT ('Godowns', RESEED, 0);

-- IMPORTANT — read before deciding whether to include this:
-- Companies.LastVoucherAlterId is the watermark the Rates & Godowns sync
-- uses to decide "full historical backfill" vs "incremental". Leaving it
-- as-is means the Agent thinks the voucher-inventory backfill already
-- happened and will only fetch NEW vouchers going forward — historical
-- rate data won't come back. Uncomment to force a fresh full backfill on
-- the next Rates & Godowns sync (this touches Companies, but only this
-- one sync-bookkeeping column, not anything about the company record):
-- UPDATE Companies SET LastVoucherAlterId = NULL;

COMMIT;
