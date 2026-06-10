IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE TABLE [Companies] (
        [CompanyId] int NOT NULL IDENTITY,
        [CompanyName] nvarchar(max) NOT NULL,
        [TallyIp] nvarchar(max) NOT NULL,
        [TallyPort] int NOT NULL,
        [TallyCompanyName] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [LastMasterSyncAt] datetime2 NULL,
        CONSTRAINT [PK_Companies] PRIMARY KEY ([CompanyId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE TABLE [SyncLogs] (
        [SyncLogId] int NOT NULL IDENTITY,
        [CompanyId] int NOT NULL,
        [SyncType] nvarchar(max) NOT NULL,
        [IsSuccess] bit NOT NULL,
        [Message] nvarchar(max) NULL,
        [SyncedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SyncLogs] PRIMARY KEY ([SyncLogId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] nvarchar(450) NOT NULL,
        [FullName] nvarchar(max) NOT NULL,
        [CompanyId] int NOT NULL,
        [Role] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUsers_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([CompanyId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE TABLE [Godowns] (
        [GodownId] int NOT NULL IDENTITY,
        [GodownName] nvarchar(450) NOT NULL,
        [CompanyId] int NOT NULL,
        [LastSyncedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Godowns] PRIMARY KEY ([GodownId]),
        CONSTRAINT [FK_Godowns_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([CompanyId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE TABLE [Ledgers] (
        [LedgerId] int NOT NULL IDENTITY,
        [LedgerName] nvarchar(450) NOT NULL,
        [Parent] nvarchar(max) NOT NULL,
        [Address] nvarchar(max) NULL,
        [MobileNo] nvarchar(max) NULL,
        [GSTNo] nvarchar(max) NULL,
        [CreditLimit] decimal(18,2) NOT NULL,
        [ClosingBalance] decimal(18,2) NOT NULL,
        [CompanyId] int NOT NULL,
        [LastSyncedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Ledgers] PRIMARY KEY ([LedgerId]),
        CONSTRAINT [FK_Ledgers_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([CompanyId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE TABLE [StockItems] (
        [StockItemId] int NOT NULL IDENTITY,
        [ItemName] nvarchar(450) NOT NULL,
        [UOM] nvarchar(max) NOT NULL,
        [Rate] decimal(18,2) NOT NULL,
        [HSNCode] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [CompanyId] int NOT NULL,
        [LastSyncedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_StockItems] PRIMARY KEY ([StockItemId]),
        CONSTRAINT [FK_StockItems_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([CompanyId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] nvarchar(450) NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE TABLE [SaleOrders] (
        [SaleOrderId] int NOT NULL IDENTITY,
        [OrderNo] nvarchar(max) NOT NULL,
        [OrderDate] datetime2 NOT NULL,
        [DeliveryDate] datetime2 NULL,
        [LedgerId] int NOT NULL,
        [LedgerName] nvarchar(max) NOT NULL,
        [CompanyId] int NOT NULL,
        [Narration] nvarchar(max) NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [Status] int NOT NULL,
        [TallyVoucherNo] nvarchar(max) NULL,
        [SyncError] nvarchar(max) NULL,
        [CreatedById] nvarchar(450) NOT NULL,
        [CreatedByName] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [SyncedAt] datetime2 NULL,
        CONSTRAINT [PK_SaleOrders] PRIMARY KEY ([SaleOrderId]),
        CONSTRAINT [FK_SaleOrders_AspNetUsers_CreatedById] FOREIGN KEY ([CreatedById]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleOrders_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([CompanyId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleOrders_Ledgers_LedgerId] FOREIGN KEY ([LedgerId]) REFERENCES [Ledgers] ([LedgerId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE TABLE [SaleOrderItems] (
        [SaleOrderItemId] int NOT NULL IDENTITY,
        [SaleOrderId] int NOT NULL,
        [StockItemId] int NOT NULL,
        [ItemName] nvarchar(max) NOT NULL,
        [UOM] nvarchar(max) NOT NULL,
        [Qty] decimal(18,3) NOT NULL,
        [Rate] decimal(18,2) NOT NULL,
        [Discount] decimal(18,2) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [GodownId] int NULL,
        [GodownName] nvarchar(max) NULL,
        CONSTRAINT [PK_SaleOrderItems] PRIMARY KEY ([SaleOrderItemId]),
        CONSTRAINT [FK_SaleOrderItems_Godowns_GodownId] FOREIGN KEY ([GodownId]) REFERENCES [Godowns] ([GodownId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleOrderItems_SaleOrders_SaleOrderId] FOREIGN KEY ([SaleOrderId]) REFERENCES [SaleOrders] ([SaleOrderId]) ON DELETE CASCADE,
        CONSTRAINT [FK_SaleOrderItems_StockItems_StockItemId] FOREIGN KEY ([StockItemId]) REFERENCES [StockItems] ([StockItemId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUsers_CompanyId] ON [AspNetUsers] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Godowns_CompanyId_GodownName] ON [Godowns] ([CompanyId], [GodownName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Ledgers_CompanyId_LedgerName] ON [Ledgers] ([CompanyId], [LedgerName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SaleOrderItems_GodownId] ON [SaleOrderItems] ([GodownId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SaleOrderItems_SaleOrderId] ON [SaleOrderItems] ([SaleOrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SaleOrderItems_StockItemId] ON [SaleOrderItems] ([StockItemId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SaleOrders_CompanyId] ON [SaleOrders] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SaleOrders_CreatedById] ON [SaleOrders] ([CreatedById]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SaleOrders_LedgerId] ON [SaleOrders] ([LedgerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StockItems_CompanyId_ItemName] ON [StockItems] ([CompanyId], [ItemName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603084750_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260603084750_InitialCreate', N'8.0.27');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603102619_AddUserCompany'
)
BEGIN
    CREATE TABLE [UserCompanies] (
        [UserCompanyId] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [CompanyId] int NOT NULL,
        CONSTRAINT [PK_UserCompanies] PRIMARY KEY ([UserCompanyId]),
        CONSTRAINT [FK_UserCompanies_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserCompanies_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([CompanyId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603102619_AddUserCompany'
)
BEGIN
    CREATE INDEX [IX_UserCompanies_CompanyId] ON [UserCompanies] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603102619_AddUserCompany'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserCompanies_UserId_CompanyId] ON [UserCompanies] ([UserId], [CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603102619_AddUserCompany'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260603102619_AddUserCompany', N'8.0.27');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603132924_AddAppSettings'
)
BEGIN
    CREATE TABLE [AppSettings] (
        [Key] nvarchar(100) NOT NULL,
        [Value] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_AppSettings] PRIMARY KEY ([Key])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603132924_AddAppSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260603132924_AddAppSettings', N'8.0.27');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [StockItems] ADD [AdditionalUnits] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [StockItems] ADD [AlterId] bigint NOT NULL DEFAULT CAST(0 AS bigint);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [StockItems] ADD [ClosingBalance] decimal(18,3) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [StockItems] ADD [ClosingValue] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [StockItems] ADD [GUID] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [StockItems] ADD [IsBatchwiseOn] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [StockItems] ADD [IsCostTrackingOn] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [StockItems] ADD [OpeningBalance] decimal(18,3) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [StockItems] ADD [OpeningValue] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [StockItems] ADD [Parent] nvarchar(max) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [StockItems] ADD [RateOfDuty] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Ledgers] ADD [AlterId] bigint NOT NULL DEFAULT CAST(0 AS bigint);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Ledgers] ADD [Email] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Ledgers] ADD [GUID] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Ledgers] ADD [IncomeTaxNo] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Ledgers] ADD [LedgerFax] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Ledgers] ADD [OpeningBalance] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Ledgers] ADD [State] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Ledgers] ADD [TaxType] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Ledgers] ADD [VATTINNo] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Godowns] ADD [Address] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Godowns] ADD [AlterId] bigint NOT NULL DEFAULT CAST(0 AS bigint);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Godowns] ADD [City] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Godowns] ADD [GUID] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Godowns] ADD [IsBatchwiseOn] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Godowns] ADD [Parent] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Godowns] ADD [PinCode] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    ALTER TABLE [Godowns] ADD [State] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603134840_MasterFieldsExpanded'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260603134840_MasterFieldsExpanded', N'8.0.27');
END;
GO

COMMIT;
GO

