-- Optional — only run this if you want the (now-unused) StockItemGodownBalances
-- table gone entirely. The app doesn't need this table to exist OR not exist;
-- it's completely disconnected from the code either way. Safe to skip.
IF OBJECT_ID('StockItemGodownBalances', 'U') IS NOT NULL
BEGIN
    DROP TABLE [StockItemGodownBalances];
END
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831161947_RemoveStockItemGodownBalances'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831161947_RemoveStockItemGodownBalances', N'8.0.29');
END
GO
