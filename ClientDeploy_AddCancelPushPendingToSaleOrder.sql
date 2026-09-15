-- Adds the two columns backing "cancel a synced Sale Order" pushing an
-- ACTION="Cancel" to the corresponding Tally voucher (SaleOrd.SyncAgent
-- picks these up in its Order cycle's new cancel-push loop).
-- Run this on the client's live SaleOrd database, then insert the matching
-- EF migration history row so `dotnet ef database update` doesn't try to
-- re-apply it.

ALTER TABLE SaleOrders ADD CancelPushPending bit NOT NULL CONSTRAINT DF_SaleOrders_CancelPushPending DEFAULT (0);
ALTER TABLE SaleOrders ADD CancelPushedAt datetime2 NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260914114538_AddCancelPushPendingToSaleOrder', N'8.0.29');
