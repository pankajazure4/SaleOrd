using Microsoft.EntityFrameworkCore;
using SaleOrd.Data;
using SaleOrd.Models.Domain;

namespace SaleOrd.Services;

// Central "whose Sale Orders can this user see" rule — used everywhere a
// controller used to just check `user.Role == AppRoles.Salesman`. Flat
// hierarchy only (see AppUser.ManagerId): a Manager sees their own orders
// plus every Salesman whose ManagerId points at them; a Manager's reports
// never themselves have reports. Admin is unrestricted (returns null, the
// "no filter" signal — never an actual huge id list) and always was.
public static class UserVisibility
{
    public static async Task<HashSet<string>?> VisibleCreatorIdsAsync(AppDbContext db, AppUser user)
    {
        if (user.Role == AppRoles.Admin) return null;

        var ids = new HashSet<string> { user.Id };

        // "Not Salesman" rather than a hardcoded Role == Manager check —
        // matches AdminController.GetManagersAsync's "Reports To" dropdown,
        // which now also allows Admin and any future custom role as a
        // target. Without this, assigning a Salesman to report to, say, a
        // future "Regional Head" role would let the dropdown pick it but
        // never actually expand that user's visible orders to include them.
        if (user.Role != AppRoles.Salesman)
        {
            var reportIds = await db.Users
                .Where(u => u.ManagerId == user.Id)
                .Select(u => u.Id)
                .ToListAsync();
            foreach (var id in reportIds) ids.Add(id);
        }
        return ids;
    }
}
