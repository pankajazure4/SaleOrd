-- Safe to run any number of times (idempotent) — only adds columns if missing.
-- Does NOT touch any existing data.
IF COL_LENGTH('Ledgers', 'IsManuallyCreated') IS NULL
BEGIN
    ALTER TABLE [Ledgers] ADD [IsManuallyCreated] bit NOT NULL DEFAULT CAST(0 AS bit);
END
GO

IF COL_LENGTH('Ledgers', 'TallyPushedAt') IS NULL
BEGIN
    ALTER TABLE [Ledgers] ADD [TallyPushedAt] datetime2 NULL;
END
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818143540_AddManualPartyTallyPushTracking'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260818143540_AddManualPartyTallyPushTracking', N'8.0.29');
END
GO
