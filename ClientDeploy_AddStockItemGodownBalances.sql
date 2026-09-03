-- Safe to run any number of times (idempotent). Creates a new table only —
-- does not touch any existing data.
IF OBJECT_ID('StockItemGodownBalances', 'U') IS NULL
BEGIN
    CREATE TABLE [StockItemGodownBalances] (
        [StockItemGodownBalanceId] int NOT NULL IDENTITY,
        [CompanyId] int NOT NULL,
        [StockItemId] int NOT NULL,
        [ItemName] nvarchar(max) NOT NULL,
        [GodownId] int NOT NULL,
        [GodownName] nvarchar(max) NOT NULL,
        [ClosingBalance] decimal(18,3) NOT NULL,
        [ClosingRate] decimal(18,2) NOT NULL,
        [ClosingValue] decimal(18,2) NOT NULL,
        [LastSyncedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_StockItemGodownBalances] PRIMARY KEY ([StockItemGodownBalanceId]),
        CONSTRAINT [FK_StockItemGodownBalances_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([CompanyId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockItemGodownBalances_Godowns_GodownId] FOREIGN KEY ([GodownId]) REFERENCES [Godowns] ([GodownId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockItemGodownBalances_StockItems_StockItemId] FOREIGN KEY ([StockItemId]) REFERENCES [StockItems] ([StockItemId]) ON DELETE NO ACTION
    );

    CREATE UNIQUE INDEX [IX_StockItemGodownBalances_CompanyId_StockItemId_GodownId] ON [StockItemGodownBalances] ([CompanyId], [StockItemId], [GodownId]);
    CREATE INDEX [IX_StockItemGodownBalances_GodownId] ON [StockItemGodownBalances] ([GodownId]);
    CREATE INDEX [IX_StockItemGodownBalances_StockItemId] ON [StockItemGodownBalances] ([StockItemId]);
END
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260828080400_AddStockItemGodownBalances'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260828080400_AddStockItemGodownBalances', N'8.0.29');
END
GO
