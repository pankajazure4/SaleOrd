using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SaleOrd.Models.Domain;

namespace SaleOrd.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Company> Companies { get; set; }
    public DbSet<Ledger> Ledgers { get; set; }
    public DbSet<StockItem> StockItems { get; set; }
    public DbSet<Godown> Godowns { get; set; }
    public DbSet<SaleOrder> SaleOrders { get; set; }
    public DbSet<SaleOrderItem> SaleOrderItems { get; set; }
    public DbSet<SyncLog> SyncLogs { get; set; }
    public DbSet<UserCompany> UserCompanies { get; set; }
    public DbSet<AppSetting> AppSettings { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }
    public DbSet<UserActivity> UserActivities { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Ledger>()
            .HasIndex(l => new { l.CompanyId, l.LedgerName })
            .IsUnique();

        builder.Entity<StockItem>()
            .HasIndex(s => new { s.CompanyId, s.ItemName })
            .IsUnique();

        builder.Entity<Godown>()
            .HasIndex(g => new { g.CompanyId, g.GodownName })
            .IsUnique();

        builder.Entity<SaleOrder>()
            .Property(o => o.TotalAmount).HasPrecision(18, 2);
        builder.Entity<SaleOrder>()
            .Property(o => o.TaxPercent).HasPrecision(10, 4);
        builder.Entity<SaleOrder>()
            .Property(o => o.IGSTTotal).HasPrecision(18, 2);
        builder.Entity<SaleOrder>()
            .Property(o => o.CGSTTotal).HasPrecision(18, 2);
        builder.Entity<SaleOrder>()
            .Property(o => o.SGSTTotal).HasPrecision(18, 2);
        builder.Entity<SaleOrder>()
            .Property(o => o.TaxTotal).HasPrecision(18, 2);
        builder.Entity<SaleOrder>()
            .Property(o => o.RoundOff).HasPrecision(18, 2);
        builder.Entity<SaleOrder>()
            .Property(o => o.GrandTotal).HasPrecision(18, 2);

        builder.Entity<SaleOrderItem>()
            .Property(i => i.Qty).HasPrecision(18, 3);
        builder.Entity<SaleOrderItem>()
            .Property(i => i.Rate).HasPrecision(18, 2);
        builder.Entity<SaleOrderItem>()
            .Property(i => i.Discount).HasPrecision(18, 2);
        builder.Entity<SaleOrderItem>()
            .Property(i => i.Amount).HasPrecision(18, 2);

        builder.Entity<Ledger>()
            .Property(l => l.ClosingBalance).HasPrecision(18, 2);
        builder.Entity<Ledger>()
            .Property(l => l.OpeningBalance).HasPrecision(18, 2);
        builder.Entity<Ledger>()
            .Property(l => l.CreditLimit).HasPrecision(18, 2);

        builder.Entity<StockItem>()
            .Property(s => s.Rate).HasPrecision(18, 2);
        builder.Entity<StockItem>()
            .Property(s => s.RateOfDuty).HasPrecision(18, 2);
        builder.Entity<StockItem>()
            .Property(s => s.OpeningBalance).HasPrecision(18, 3);
        builder.Entity<StockItem>()
            .Property(s => s.ClosingBalance).HasPrecision(18, 3);
        builder.Entity<StockItem>()
            .Property(s => s.OpeningValue).HasPrecision(18, 2);
        builder.Entity<StockItem>()
            .Property(s => s.ClosingValue).HasPrecision(18, 2);

        builder.Entity<AppUser>()
            .HasOne(u => u.Company)
            .WithMany(c => c.Users)
            .HasForeignKey(u => u.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Prevent cascade cycles — SQL Server does not allow multiple cascade paths
        builder.Entity<Ledger>()
            .HasOne(l => l.Company)
            .WithMany(c => c.Ledgers)
            .HasForeignKey(l => l.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockItem>()
            .HasOne(s => s.Company)
            .WithMany(c => c.StockItems)
            .HasForeignKey(s => s.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Godown>()
            .HasOne(g => g.Company)
            .WithMany(c => c.Godowns)
            .HasForeignKey(g => g.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SaleOrder>()
            .HasOne(o => o.Company)
            .WithMany(c => c.SaleOrders)
            .HasForeignKey(o => o.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SaleOrder>()
            .HasOne(o => o.Ledger)
            .WithMany()
            .HasForeignKey(o => o.LedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SaleOrder>()
            .HasOne(o => o.CreatedBy)
            .WithMany()
            .HasForeignKey(o => o.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SaleOrderItem>()
            .HasOne(i => i.StockItem)
            .WithMany()
            .HasForeignKey(i => i.StockItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SaleOrderItem>()
            .HasOne(i => i.Godown)
            .WithMany()
            .HasForeignKey(i => i.GodownId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<UserCompany>()
            .HasIndex(uc => new { uc.UserId, uc.CompanyId })
            .IsUnique();

        builder.Entity<UserCompany>()
            .HasOne(uc => uc.User)
            .WithMany(u => u.UserCompanies)
            .HasForeignKey(uc => uc.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<UserCompany>()
            .HasOne(uc => uc.Company)
            .WithMany()
            .HasForeignKey(uc => uc.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<RolePermission>()
            .HasIndex(rp => new { rp.Role, rp.PermissionKey })
            .IsUnique();
    }
}
