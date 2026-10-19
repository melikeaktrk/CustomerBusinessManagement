using CustomerBusinessManagement.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CustomerBusinessManagement.DataAccess;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<AccountingTransaction> AccountingTransactions => Set<AccountingTransaction>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<ZReport> ZReports => Set<ZReport>();
    public DbSet<EmployeeLeaveRecord> EmployeeLeaveRecords => Set<EmployeeLeaveRecord>();
    public DbSet<EmployeeLeaveBalance> EmployeeLeaveBalances => Set<EmployeeLeaveBalance>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Customer>().HasIndex(x => x.CustomerCode).IsUnique();
        b.Entity<Employee>().Property(x => x.Salary).HasPrecision(18, 2);
        b.Entity<AccountingTransaction>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<CashTransaction>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<ZReport>().Property(x => x.GrossSales).HasPrecision(18, 2);
        b.Entity<ZReport>().Property(x => x.TotalVat).HasPrecision(18, 2);
        b.Entity<ZReport>().Property(x => x.NetSales).HasPrecision(18, 2);
        b.Entity<ZReport>().Property(x => x.CashAmount).HasPrecision(18, 2);
        b.Entity<ZReport>().Property(x => x.CardAmount).HasPrecision(18, 2);
        b.Entity<ZReport>().Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Entity<ZReport>().Property(x => x.KdvToplami).HasPrecision(18, 2);
        b.Entity<ZReport>().Property(x => x.SatisToplami).HasPrecision(18, 2);
        b.Entity<ZReport>().Property(x => x.Nakit).HasPrecision(18, 2);
        b.Entity<ZReport>().Property(x => x.Kredi).HasPrecision(18, 2);
        b.Entity<ZReport>().Property(x => x.Diger).HasPrecision(18, 2);
        b.Entity<ZReport>().Property(x => x.IptalKdvToplami).HasPrecision(18, 2);
        b.Entity<ZReport>().Property(x => x.IptalSatisToplami).HasPrecision(18, 2);
        b.Entity<EmployeeLeaveRecord>().HasIndex(x => new { x.EmployeeId, x.LeaveStartDate, x.LeaveEndDate });
        b.Entity<EmployeeLeaveBalance>().HasIndex(x => new { x.EmployeeId, x.LeaveYear }).IsUnique();
        b.Entity<EmployeeLeaveRecord>().Property(x => x.NumberOfDays).HasPrecision(5, 0);
        b.Entity<EmployeeLeaveBalance>().Property(x => x.EntitlementDays).HasPrecision(5, 0);
        b.Entity<EmployeeLeaveBalance>().Property(x => x.UsedDays).HasPrecision(5, 0);
        b.Entity<EmployeeLeaveBalance>().Property(x => x.RemainingDays).HasPrecision(5, 0);
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (
            var e in ChangeTracker.Entries<Entity>().Where(x => x.State == EntityState.Modified)
        )
            e.Entity.UpdatedAt = DateTime.UtcNow;
        return base.SaveChangesAsync(ct);
    }
}
