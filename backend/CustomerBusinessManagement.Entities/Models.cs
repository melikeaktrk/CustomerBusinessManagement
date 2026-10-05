using Microsoft.AspNetCore.Identity;

namespace CustomerBusinessManagement.Entities;

public abstract class Entity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public sealed class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

public sealed class Customer : Entity
{
    public string CustomerCode { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? CompanyName { get; set; }
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public string? TaxNumber { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Employee : Entity
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Position { get; set; } = "";
    public DateOnly HireDate { get; set; }
    public decimal Salary { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum AccountingType
{
    Income,
    Expense,
}

public sealed class AccountingTransaction : Entity
{
    public AccountingType TransactionType { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    public DateTime TransactionDate { get; set; }
    public string Category { get; set; } = "";
    public string? ReferenceNumber { get; set; }
    public bool IsCancelled { get; set; }
}

public enum CashType
{
    CashIn,
    CashOut,
}

public sealed class CashTransaction : Entity
{
    public CashType TransactionType { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    public DateTime TransactionDate { get; set; }
    public string? ReferenceNumber { get; set; }
    public bool IsCancelled { get; set; }
}

public sealed class ZReport : Entity
{
    public DateTime ReportDate { get; set; }
    public decimal GrossSales { get; set; }
    public decimal TotalVat { get; set; }
    public decimal NetSales { get; set; }
    public decimal CashAmount { get; set; }
    public decimal CardAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? DocumentPath { get; set; }
    public string? OcrRawText { get; set; }
    public bool IsConfirmed { get; set; }
}

public sealed class AuditLog : Entity
{
    public string Action { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Description { get; set; } = "";
    public string? UserId { get; set; }
}
