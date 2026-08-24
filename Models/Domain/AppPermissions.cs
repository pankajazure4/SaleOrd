namespace SaleOrd.Models.Domain;

public static class AppPermissions
{
    // General
    public const string Dashboard        = "Dashboard";

    // Sale Orders
    public const string SaleOrderView    = "SaleOrder.View";
    public const string SaleOrderCreate  = "SaleOrder.Create";
    public const string SaleOrderEdit    = "SaleOrder.Edit";
    public const string SaleOrderCancel  = "SaleOrder.Cancel";

    // Reports - Sales
    public const string ReportsSummary   = "Reports.Summary";
    public const string ReportsPartyWise = "Reports.PartyWise";
    public const string ReportsDayWise   = "Reports.DayWise";
    public const string ReportsUserWise  = "Reports.UserWise";
    public const string ReportsPending   = "Reports.Pending";
    public const string ReportsUserPerf  = "Reports.UserPerf";
    public const string ReportsTransactionCompare = "Reports.TransactionCompare";

    // Masters
    public const string MastersParties   = "Masters.Parties";
    public const string MastersItems     = "Masters.Items";

    // Prints
    public const string PrintSaleOrder   = "Print.SaleOrder";

    // Export — separate from the Reports.* view permissions above on
    // purpose: a role can be allowed to VIEW a report without being allowed
    // to pull its data out of the system as a file.
    public const string ReportsExport    = "Reports.Export";

    // Admin
    public const string AdminUserActivity = "Admin.UserActivity";

    public static readonly (string Key, string Label, string Group)[] All =
    {
        (Dashboard,          "Dashboard",                     "General"),
        (SaleOrderView,      "Sale Orders - View",            "Sale Orders"),
        (SaleOrderCreate,    "Sale Orders - Create",          "Sale Orders"),
        (SaleOrderEdit,      "Sale Orders - Edit",            "Sale Orders"),
        (SaleOrderCancel,    "Sale Orders - Cancel",          "Sale Orders"),
        (ReportsSummary,     "Summary Stats",                 "Reports"),
        (ReportsPartyWise,   "Party-wise Report",             "Reports"),
        (ReportsDayWise,     "Day-wise Report",               "Reports"),
        (ReportsUserWise,    "User-wise Report",              "Reports"),
        (ReportsPending,     "Pending Orders Report",         "Reports"),
        (ReportsUserPerf,    "User Performance Report",       "Reports"),
        (ReportsTransactionCompare, "Order vs Invoice Report", "Reports"),
        (MastersParties,     "Parties",                       "Masters"),
        (MastersItems,       "Stock Items",                   "Masters"),
        (PrintSaleOrder,     "Print Sale Order",              "Prints"),
        (ReportsExport,      "Export Reports (CSV)",          "Prints"),
        (AdminUserActivity,  "View User Activity",            "Admin"),
    };

    public static readonly string[] ManagerDefaults = All.Select(p => p.Key).ToArray();

    public static readonly string[] SalesmanDefaults =
    {
        Dashboard,
        SaleOrderView,
        SaleOrderCreate,
        SaleOrderEdit,
        SaleOrderCancel,
        ReportsSummary,
        ReportsPartyWise,
        ReportsDayWise,
        ReportsTransactionCompare,
        MastersParties,
        MastersItems,
        PrintSaleOrder,
    };
}
