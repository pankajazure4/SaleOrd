-- Adds the "Reports To" hierarchy field on AspNetUsers — a Salesman's
-- Manager (self-referencing FK). Backs Manager-scoped dashboards/reports
-- (UserVisibility.VisibleCreatorIdsAsync) and the Reports To picker in
-- Admin > Create/Edit User and the Signup Requests approval modal.
-- Run this on the client's live SaleOrd database, then insert the matching
-- EF migration history row so `dotnet ef database update` doesn't try to
-- re-apply it.

ALTER TABLE AspNetUsers ADD ManagerId nvarchar(450) NULL;

CREATE INDEX IX_AspNetUsers_ManagerId ON AspNetUsers (ManagerId);

ALTER TABLE AspNetUsers ADD CONSTRAINT FK_AspNetUsers_AspNetUsers_ManagerId
    FOREIGN KEY (ManagerId) REFERENCES AspNetUsers (Id) ON DELETE NO ACTION;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260914115617_AddManagerIdToAppUser', N'8.0.29');
