
GO
/****** Object:  Table [dbo].[SyncQueries]    Script Date: 03-06-2026 19:11:10 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[SyncQueries](
	[TableName] [nvarchar](100) NOT NULL,
	[QueryText] [nvarchar](max) NOT NULL,
	[SyncOrder] [int] NULL,
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Columnnames] [nvarchar](max) NULL,
	[IsTransaction] [bit] NOT NULL,
	[Issync] [bit] NULL,
	[AlterQueryText] [nvarchar](max) NULL,
PRIMARY KEY CLUSTERED 
(
	[TableName] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
SET IDENTITY_INSERT [dbo].[SyncQueries] ON 
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'BankAllocation', N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>Export</TALLYREQUEST>
        <TYPE>Collection</TYPE>
        <ID>VoucherBankAllocations</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVFROMDATE TYPE=''Date''>pstartingdate</SVFROMDATE>
                <SVTODATE TYPE=''Date''>pendingdate</SVTODATE>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                <EXPLODEVCHTYPE>Yes</EXPLODEVCHTYPE>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="VoucherBankAllocations" ISINITIALIZE=''Yes''>
                        <TYPE>Voucher</TYPE>
                        <FETCH>
                            GUID,
                            VoucherNumber,
                            VoucherTypeName,
                            ALLLEDGERENTRIES.LIST:LedgerName,
                            ALLLEDGERENTRIES.LIST:BANKALLOCATIONS.LIST:Name,
                            ALLLEDGERENTRIES.LIST:BANKALLOCATIONS.LIST:TransactionType,
                            ALLLEDGERENTRIES.LIST:BANKALLOCATIONS.LIST:InstrumentNumber,
                            ALLLEDGERENTRIES.LIST:BANKALLOCATIONS.LIST:PaymentFavouring,
                            ALLLEDGERENTRIES.LIST:BANKALLOCATIONS.LIST:Amount
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>', 16, 147, N'', 1, 1, N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>Export</TALLYREQUEST>
        <TYPE>Collection</TYPE>
        <ID>VoucherBankAllocations</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                <EXPLODEVCHTYPE>Yes</EXPLODEVCHTYPE>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="VoucherBankAllocations" ISINITIALIZE=''Yes''>
                        <TYPE>Voucher</TYPE>
                        <FETCH>
                            GUID,
                            VoucherNumber,
                            VoucherTypeName,
                            ALLLEDGERENTRIES.LIST:LedgerName,
                            ALLLEDGERENTRIES.LIST:BANKALLOCATIONS.LIST:Name,
                            ALLLEDGERENTRIES.LIST:BANKALLOCATIONS.LIST:TransactionType,
                            ALLLEDGERENTRIES.LIST:BANKALLOCATIONS.LIST:InstrumentNumber,
                            ALLLEDGERENTRIES.LIST:BANKALLOCATIONS.LIST:PaymentFavouring,
                            ALLLEDGERENTRIES.LIST:BANKALLOCATIONS.LIST:Amount
                        </FETCH>
                        <FILTER>DateFilter</FILTER>
                    </COLLECTION>
                    <SYSTEM TYPE="Formulae" NAME="DateFilter">$AlterId > plastsyncalterid</SYSTEM>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>')
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'BillAllocation', N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>Export</TALLYREQUEST>
        <TYPE>Collection</TYPE>
        <ID>VoucherBills</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVFROMDATE TYPE=''Date''>pstartingdate</SVFROMDATE>
                <SVTODATE TYPE=''Date''>pendingdate</SVTODATE>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                <EXPLODEVCHTYPE>Yes</EXPLODEVCHTYPE>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="VoucherBills" ISINITIALIZE=''Yes''>
                        <TYPE>Voucher</TYPE>
                        <FETCH>
                            GUID,
                            ALLLEDGERENTRIES.LIST:LedgerName,
                            ALLLEDGERENTRIES.LIST:BILLALLOCATIONS.LIST:Name,
                            ALLLEDGERENTRIES.LIST:BILLALLOCATIONS.LIST:Amount,
                            ALLLEDGERENTRIES.LIST:BILLALLOCATIONS.LIST:BillType,
                            ALLLEDGERENTRIES.LIST:BILLALLOCATIONS.LIST:BillCreditPeriod
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>', 15, 143, N'', 1, 1, N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>Export</TALLYREQUEST>
        <TYPE>Collection</TYPE>
        <ID>VoucherBills</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                <EXPLODEVCHTYPE>Yes</EXPLODEVCHTYPE>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="VoucherBills" ISINITIALIZE=''Yes''>
                        <TYPE>Voucher</TYPE>
                        <FETCH>
                            GUID,
                            ALLLEDGERENTRIES.LIST:LedgerName,
                            ALLLEDGERENTRIES.LIST:BILLALLOCATIONS.LIST:Name,
                            ALLLEDGERENTRIES.LIST:BILLALLOCATIONS.LIST:Amount,
                            ALLLEDGERENTRIES.LIST:BILLALLOCATIONS.LIST:BillType,
                            ALLLEDGERENTRIES.LIST:BILLALLOCATIONS.LIST:BillCreditPeriod
                        </FETCH>
                        <FILTER>DateFilter</FILTER>
                    </COLLECTION>
                    <SYSTEM TYPE="Formulae" NAME="DateFilter">$AlterId > plastsyncalterid</SYSTEM>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>')
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'Budgets', N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Budgets</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of Budgets" ISINITIALIZE="Yes">
                        <TYPE>Budget</TYPE>
                        <FETCH>Name,Parent,GUID,AlterId,StartingFrom,ForClosingBalance,ForRevenue,ForNonRevenue,IsGroup</FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>
', 9, 1, N'Name,Parent,GUID,AlterId,StartingFrom,ForClosingBalance,ForRevenue,ForNonRevenue,IsGroup', 0, 0, N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Budgets</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of Budgets" ISINITIALIZE="Yes">
                        <TYPE>Budget</TYPE>
                        <FETCH>Name,Parent,GUID,AlterId,StartingFrom,ForClosingBalance,ForRevenue,ForNonRevenue,IsGroup</FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>
')
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'Company', N'<ENVELOPE><HEADER><VERSION>1</VERSION><TALLYREQUEST>EXPORT</TALLYREQUEST><TYPE>COLLECTION</TYPE><ID>List of Companies</ID></HEADER><BODY><DESC><STATICVARIABLES><SVCURRENTCOMPANY>Money2me Finance Private Limited</SVCURRENTCOMPANY><SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT></STATICVARIABLES><TDL><TDLMESSAGE>                      <COLLECTION NAME="List of Companies" ISINITIALIZE="Yes">                          <TYPE>Company</TYPE><FETCH>Name,GUID,Alterid,StartingFrom,CompanyNumber,EndingAt,Isactive</FETCH></COLLECTION></TDLMESSAGE></TDL></DESC></BODY></ENVELOPE>', 1, 2, N'Name,GUID,Alterid,StartingFrom,CompanyNumber,EndingAt,Isactive', 0, 0, N'<ENVELOPE><HEADER><VERSION>1</VERSION><TALLYREQUEST>EXPORT</TALLYREQUEST><TYPE>COLLECTION</TYPE><ID>List of Companies</ID></HEADER><BODY><DESC><STATICVARIABLES><SVCURRENTCOMPANY>Money2me Finance Private Limited</SVCURRENTCOMPANY><SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT></STATICVARIABLES><TDL><TDLMESSAGE><COLLECTION NAME="List of Companies" ISINITIALIZE="Yes"><TYPE>Company</TYPE><FETCH>Name,GUID,Alterid,StartingFrom,CompanyNumber</FETCH><FILTER>MyDateFilter</FILTER></COLLECTION><SYSTEM TYPE="Formulae" NAME="MyDateFilter">$AlterId>plastsyncalterid</SYSTEM></TDLMESSAGE></TDL></DESC></BODY>
</ENVELOPE>')
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'CostCategory', N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of CostCategories</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of CostCategories" ISINITIALIZE="Yes">
                        <TYPE>CostCategories</TYPE>
                        <FETCH>Name,GUID,AlterID,IsRevenue,IsFixedCost
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>
', 6, 3, N'Name,GUID,AlterID,IsRevenue,IsFixedCost', 0, 0, N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of CostCategories</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of CostCategories" ISINITIALIZE="Yes">
                        <TYPE>CostCategories</TYPE>
                        <FETCH>Name,GUID,AlterID,IsRevenue,IsFixedCost
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>
')
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'CostCentres', N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of CostCentres</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of CostCentres" ISINITIALIZE="Yes">
                        <TYPE>CostCentres</TYPE>
                        <FETCH>Name,Parent,GUID,AlterID,Category,IsRevenue,IsFixedCost
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>
', 7, 4, N'Name,Parent,GUID,AlterID,Category,IsRevenue,IsFixedCost', 0, 0, N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of CostCentres</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of CostCentres" ISINITIALIZE="Yes">
                        <TYPE>CostCentres</TYPE>
                        <FETCH>Name,Parent,GUID,AlterID,Category,IsRevenue,IsFixedCost
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>
')
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'Currencies', N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Currencies</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of Currencies" ISINITIALIZE="Yes">
                        <TYPE>Currencies</TYPE>
                        <FETCH>Name,GUID,AlterID,Symbol,FormalName,IsBaseCurrency,DecimalPlaces,SubUnits
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>
', 8, 5, N'Name,GUID,AlterID,Symbol,FormalName,IsBaseCurrency,DecimalPlaces,SubUnits', 0, 0, N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Currencies</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of Currencies" ISINITIALIZE="Yes">
                        <TYPE>Currencies</TYPE>
                        <FETCH>Name,GUID,AlterID,Symbol,FormalName,IsBaseCurrency,DecimalPlaces,SubUnits
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>
')
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'Godowns', N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Godowns</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of Godowns" ISINITIALIZE="Yes">
                        <TYPE>Godown</TYPE>
                        <FETCH>
                            Name,Parent,GUID,AlterId,IsCostCentresOn,IsRevenue,IsDeemedPositive,
                            IsAddQty,IsBatchesOn,Address,City,State,PinCode
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>
', 12, 6, N' Name,Parent,GUID,AlterId,IsCostCentresOn,IsRevenue,IsDeemedPositive,IsAddQty,IsBatchesOn,Address,City,State,PinCode', 0, 0, N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Godowns</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of Godowns" ISINITIALIZE="Yes">
                        <TYPE>Godown</TYPE>
                        <FETCH>
                            Name,Parent,GUID,AlterId,IsCostCentresOn,IsRevenue,IsDeemedPositive,
                            IsAddQty,IsBatchesOn,Address,City,State,PinCode
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>
')
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'Groups', N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Groups</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
            <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of Groups" ISINITIALIZE="Yes">
                        <TYPE>Groups</TYPE>
                        <FETCH>Name,Parent,GUID,Alterid,OpeningBalance,ClosingBalance,_Grandparent,IsSubLedger,Nature</FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>', 2, 7, N'Name,Parent,GUID,Alterid,OpeningBalance,ClosingBalance,_Grandparent,IsSubLedger,Nature', 0, 0, N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Groups</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
            <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of Groups" ISINITIALIZE="Yes">
                        <TYPE>Groups</TYPE>
                        <FETCH>Name,Parent,GUID,Alterid,OpeningBalance,ClosingBalance,_Grandparent,IsSubLedger,Nature</FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>')
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'Ledger', N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Ledger</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
            <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of Ledger" ISINITIALIZE="Yes">
                        <TYPE>Ledger</TYPE>
                        <FETCH>Name,Parent,_Address1,_Address2,_Adress3,_Adress4,_Address5,PriorStateName, 
LedgerContact,LedgerPhone,SalesTaxNumber,_CentralTax,IncomeTaxNumber,VATTINNUMBER,OpeningBalance,TaxType,LedgerFax,AlteredOn,AlteredBy,
TaxClassificationName,Email,CreditLimit,PartyGSTIN,GUID,AlterId,CreatedDate</FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>', 3, 8, N'Name,Parent,_Address1,_Address2,_Adress3,_Adress4,_Address5,PriorStateName, 
LedgerContact,LedgerPhone,SalesTaxNumber,_CentralTax,IncomeTaxNumber,VATTINNUMBER,OpeningBalance,TaxType,LedgerFax,AlteredOn,AlteredBy,
TaxClassificationName,Email,CreditLimit,PartyGSTIN,GUID,AlterId,CreatedDate', 0, 0, N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Ledger</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
            <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of Ledger" ISINITIALIZE="Yes">
                        <TYPE>Ledger</TYPE>
                        <FETCH>Name,Parent,_Address1,_Address2,_Adress3,_Adress4,_Address5,PriorStateName, 
LedgerContact,LedgerPhone,SalesTaxNumber,_CentralTax,IncomeTaxNumber,VATTINNUMBER,OpeningBalance,TaxType,LedgerFax,AlteredOn,AlteredBy,
TaxClassificationName,Email,CreditLimit,PartyGSTIN,GUID,AlterId,CreatedDate</FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>')
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'Stockgroups', N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Stock Groups</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of Stock Groups" ISINITIALIZE="Yes">
                        <TYPE>Stock Group</TYPE>
                        <FETCH>Name,Parent,GUID,AlterId,BaseUnits,AddlUnits,IsAddQty,IsRevenue,IsDeemedPositive,CalculationType</FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>
', 10, 9, N'Name,Parent,GUID,AlterId,BaseUnits,AddlUnits,IsAddQty,IsRevenue,IsDeemedPositive,CalculationType', 0, 0, N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Stock Groups</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of Stock Groups" ISINITIALIZE="Yes">
                        <TYPE>Stock Group</TYPE>
                        <FETCH>Name,Parent,GUID,AlterId,BaseUnits,AddlUnits,IsAddQty,IsRevenue,IsDeemedPositive,CalculationType</FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>
')
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'Stockitems', N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Stock Items</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of Stock Items" ISINITIALIZE="Yes">
                        <TYPE>Stock Item</TYPE>
                        <FETCH>
                            Name,Parent,GUID,AlterId,BaseUnits,AdditionalUnits,
                            _Conversion,RateOfDuty,OpeningBalance,ClosingBalance,
                            OpeningValue,ClosingValue,IsBatchwiseOn,IsCostTrackingOn,
                            IsRevenue,IsDeemedPositive,
                            {{UDF_FIELDS}}
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>', 11, 10, N'Name,Parent,GUID,AlterId,BaseUnits,AdditionalUnits,_Conversion,RateOfDuty,OpeningBalance,ClosingBalance,OpeningValue,ClosingValue,IsBatchwiseOn,IsCostTrackingOn,IsRevenue,IsDeemedPositive', 0, 0, N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Stock Items</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of Stock Items" ISINITIALIZE="Yes">
                        <TYPE>Stock Item</TYPE>
                        <FETCH>
                            Name,Parent,GUID,AlterId,BaseUnits,AdditionalUnits,
                            _Conversion,RateOfDuty,OpeningBalance,ClosingBalance,
                            OpeningValue,ClosingValue,IsBatchwiseOn,IsCostTrackingOn,
                            IsRevenue,IsDeemedPositive,
                            {{UDF_FIELDS}}
                        </FETCH>
                        <FILTER>MyDateFilter</FILTER>
                    </COLLECTION>
                    <SYSTEM TYPE="Formulae" NAME="MyDateFilter">$AlterId>plastsyncalterid</SYSTEM>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>')
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'Units', N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Units</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of Units" ISINITIALIZE="Yes">
                        <TYPE>Unit</TYPE>
                        <FETCH>
                            Name,GUID,AlterId,OriginalName,FormalName,UnitQuantityCode,
                            DecimalPlaces,IsSimpleUnit,IsCompoundUnit,BaseUnit,AdditionalUnits,Conversion
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>
', 4, 11, N'Name,GUID,AlterId,OriginalName,FormalName,UnitQuantityCode,DecimalPlaces,IsSimpleUnit,IsCompoundUnit,BaseUnit,AdditionalUnits,Conversion', 0, 0, N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Units</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of Units" ISINITIALIZE="Yes">
                        <TYPE>Unit</TYPE>
                        <FETCH>
                            Name,GUID,AlterId,OriginalName,FormalName,UnitQuantityCode,
                            DecimalPlaces,IsSimpleUnit,IsCompoundUnit,BaseUnit,AdditionalUnits,Conversion
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>
')
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'Voucher', N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>Export</TALLYREQUEST>
        <TYPE>Collection</TYPE>
        <ID>VoucherHeaders</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVFROMDATE TYPE=''Date''>pstartingdate</SVFROMDATE>
                <SVTODATE TYPE=''Date''>pendingdate</SVTODATE>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                <EXPLODEVCHTYPE>Yes</EXPLODEVCHTYPE>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="VoucherHeaders" ISINITIALIZE="Yes">
                        <TYPE>Voucher</TYPE>
                        <FETCH>
                            Date,
                            VoucherTypeName,
                            VoucherNumber,
                            GUID,
                            AlterID,
                            Narration,
                            PartyLedgerName,
                            IsCancelled,
                            IsOptional,
                            IsInvoice,
                            EffectiveDate,
                            {{UDF_FIELDS}}
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>', 13, 48, N'', 1, 1, N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>Export</TALLYREQUEST>
        <TYPE>Collection</TYPE>
        <ID>VoucherHeaders</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                <EXPLODEVCHTYPE>Yes</EXPLODEVCHTYPE>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="VoucherHeaders" ISINITIALIZE="Yes">
                        <TYPE>Voucher</TYPE>
                        <FETCH>
                            Date,
                            VoucherTypeName,
                            VoucherNumber,
                            GUID,
                            AlterID,
                            Narration,
                            PartyLedgerName,
                            IsCancelled,
                            IsOptional,
                            IsInvoice,
                            EffectiveDate,
                            {{UDF_FIELDS}}
                        </FETCH>
                        <FILTER>DateFilter</FILTER>
                    </COLLECTION>
                    <SYSTEM TYPE="Formulae" NAME="DateFilter">$AlterId > plastsyncalterid</SYSTEM>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>')
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'VoucherInventories', N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>Export</TALLYREQUEST>
        <TYPE>Collection</TYPE>
        <ID>VoucherInventory</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVFROMDATE TYPE=''Date''>pstartingdate</SVFROMDATE>
                <SVTODATE TYPE=''Date''>pendingdate</SVTODATE>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                <EXPLODEVCHTYPE>Yes</EXPLODEVCHTYPE>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="VoucherInventory" ISINITIALIZE=''Yes''>
                        <TYPE>Voucher</TYPE>
                        <FETCH>
                            GUID,
                            VoucherNumber,
                            VoucherTypeName,
                            ALLINVENTORYENTRIES.LIST:StockItemName,
                            ALLINVENTORYENTRIES.LIST:ActualQty,
                            ALLINVENTORYENTRIES.LIST:BilledQty,
                            ALLINVENTORYENTRIES.LIST:Rate,
                            ALLINVENTORYENTRIES.LIST:Amount,
                            ALLINVENTORYENTRIES.LIST:Discount,
                            ALLINVENTORYENTRIES.LIST:BATCHALLOCATIONS.LIST:GodownName,
                            {{UDF_FIELDS}}
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>', 17, 148, N'', 1, 1, N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>Export</TALLYREQUEST>
        <TYPE>Collection</TYPE>
        <ID>VoucherInventory</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                <EXPLODEVCHTYPE>Yes</EXPLODEVCHTYPE>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="VoucherInventory" ISINITIALIZE=''Yes''>
                        <TYPE>Voucher</TYPE>
                        <FETCH>
                            GUID,
                            VoucherNumber,
                            VoucherTypeName,
                            ALLINVENTORYENTRIES.LIST:StockItemName,
                            ALLINVENTORYENTRIES.LIST:ActualQty,
                            ALLINVENTORYENTRIES.LIST:BilledQty,
                            ALLINVENTORYENTRIES.LIST:Rate,
                            ALLINVENTORYENTRIES.LIST:Amount,
                            ALLINVENTORYENTRIES.LIST:Discount,
                            ALLINVENTORYENTRIES.LIST:BATCHALLOCATIONS.LIST:GodownName,
                            {{UDF_FIELDS}}
                        </FETCH>
                        <FILTER>DateFilter</FILTER>
                    </COLLECTION>
                    <SYSTEM TYPE="Formulae" NAME="DateFilter">$AlterId > plastsyncalterid</SYSTEM>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>')
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'Voucherledgers', N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>Export</TALLYREQUEST>
        <TYPE>Collection</TYPE>
        <ID>VoucherLedgers</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVFROMDATE TYPE=''Date''>pstartingdate</SVFROMDATE>
                <SVTODATE TYPE=''Date''>pendingdate</SVTODATE>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                <EXPLODEVCHTYPE>Yes</EXPLODEVCHTYPE>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="VoucherLedgers" ISINITIALIZE="Yes">
                        <TYPE>Voucher</TYPE>
                        <FETCH>
                            GUID,
                            ALLLEDGERENTRIES.LIST:LedgerName,
                            ALLLEDGERENTRIES.LIST:Amount,
                            ALLLEDGERENTRIES.LIST:IsDeemedPositive,
                            ALLLEDGERENTRIES.LIST:CostCentreName
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>', 14, 142, N'', 1, 1, N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>Export</TALLYREQUEST>
        <TYPE>Collection</TYPE>
        <ID>VoucherLedgers</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                <EXPLODEVCHTYPE>Yes</EXPLODEVCHTYPE>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="VoucherLedgers" ISINITIALIZE="Yes">
                        <TYPE>Voucher</TYPE>
                        <FETCH>
                            GUID,
                            ALLLEDGERENTRIES.LIST:LedgerName,
                            ALLLEDGERENTRIES.LIST:Amount,
                            ALLLEDGERENTRIES.LIST:IsDeemedPositive,
                            ALLLEDGERENTRIES.LIST:CostCentreName
                        </FETCH>
                        <FILTER>DateFilter</FILTER>
                    </COLLECTION>
                    <SYSTEM TYPE="Formulae" NAME="DateFilter">$AlterId > plastsyncalterid</SYSTEM>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>')
GO
INSERT [dbo].[SyncQueries] ([TableName], [QueryText], [SyncOrder], [Id], [Columnnames], [IsTransaction], [Issync], [AlterQueryText]) VALUES (N'Vouchertype', N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of VoucherTypes</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of VoucherTypes" ISINITIALIZE="Yes">
                        <TYPE>VoucherTypes</TYPE>
                        <FETCH>Name,Parent,GUID,AlterId,Abbreviation,
                               NumberingMethod,IsInvoice,UseEffectiveDates,
                               OptionalByDefault,AllowNarration,NarrationPerLedger,
                               PrintAfterSave,DefaultTitleForPrint,DefaultBank,
                               Jurisdiction,UseForPOS,IsCancelled,IsActive
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>
', 5, 12, N'Name,Parent,GUID,AlterId,Abbreviation,NumberingMethod,IsInvoice,UseEffectiveDates,OptionalByDefault,AllowNarration,NarrationPerLedger,PrintAfterSave,DefaultTitleForPrint,DefaultBank,Jurisdiction,UseForPOS,IsCancelled,IsActive', 0, 0, N'<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of VoucherTypes</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>pcurrentcompname</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME="List of VoucherTypes" ISINITIALIZE="Yes">
                        <TYPE>VoucherTypes</TYPE>
                        <FETCH>Name,Parent,GUID,AlterId,Abbreviation,
                               NumberingMethod,IsInvoice,UseEffectiveDates,
                               OptionalByDefault,AllowNarration,NarrationPerLedger,
                               PrintAfterSave,DefaultTitleForPrint,DefaultBank,
                               Jurisdiction,UseForPOS,IsCancelled,IsActive
                        </FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>
')
GO
SET IDENTITY_INSERT [dbo].[SyncQueries] OFF
GO
ALTER TABLE [dbo].[SyncQueries] ADD  DEFAULT ((0)) FOR [IsTransaction]
GO
