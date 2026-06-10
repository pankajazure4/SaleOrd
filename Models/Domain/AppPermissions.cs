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

    // Masters
    public const string MastersParties   = "Masters.Parties";
    public const string MastersItems     = "Masters.Items";

    // Prints
    public const string PrintSaleOrder   = "Print.SaleOrder";

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
        (MastersParties,     "Parties",                       "Masters"),
        (MastersItems,       "Stock Items",                   "Masters"),
        (PrintSaleOrder,     "Print Sale Order",              "Prints"),
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
        MastersParties,
        MastersItems,
        PrintSaleOrder,
    };
}
