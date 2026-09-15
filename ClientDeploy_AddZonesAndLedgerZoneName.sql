-- Adds the Zone feature: a per-company fixed list (Zones) an Admin manages
-- from Settings, picked from at Party Approval (Admin > Pending Parties),
-- denormalized onto Ledgers.ZoneName and pushed to Tally as a UDF field if
-- Settings > Order Defaults > Tally Zone UDF Field is configured.
-- Run this on the client's live SaleOrd database, then insert the matching
-- EF migration history row so `dotnet ef database update` doesn't try to
-- re-apply it.

ALTER TABLE Ledgers ADD ZoneName nvarchar(max) NULL;

CREATE TABLE Zones (
    ZoneId    int IDENTITY(1,1) NOT NULL,
    ZoneName  nvarchar(450) NOT NULL,
    CompanyId int NOT NULL,
    IsActive  bit NOT NULL,
    CONSTRAINT PK_Zones PRIMARY KEY (ZoneId),
    CONSTRAINT FK_Zones_Companies_CompanyId FOREIGN KEY (CompanyId)
        REFERENCES Companies (CompanyId) ON DELETE NO ACTION
);

CREATE UNIQUE INDEX IX_Zones_CompanyId_ZoneName ON Zones (CompanyId, ZoneName);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260914120117_AddZonesAndLedgerZoneName', N'8.0.29');
