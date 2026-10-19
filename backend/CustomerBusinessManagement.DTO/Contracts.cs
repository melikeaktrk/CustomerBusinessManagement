using System.ComponentModel.DataAnnotations;

namespace CustomerBusinessManagement.DTO;

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);

public record LoginResponse(
    string Token,
    DateTime ExpiresAt,
    string UserId,
    string Email,
    string UserName
);

public sealed class CustomerRequest
{
    [Required]
    public string CustomerCode { get; set; } = "";

    [Required]
    public string FirstName { get; set; } = "";

    [Required]
    public string LastName { get; set; } = "";

    public string? CompanyName { get; set; }

    [Required]
    public string Phone { get; set; } = "";

    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Address { get; set; } = "";

    public string? TaxNumber { get; set; }

    public DateOnly CustomerRegistrationDate { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class EmployeeRequest
{
    [Required]
    public string FirstName { get; set; } = "";

    [Required]
    public string LastName { get; set; } = "";

    [Required]
    public string Phone { get; set; } = "";

    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Position { get; set; } = "";

    public DateOnly HireDate { get; set; }

    public DateOnly EmployeeRegistrationDate { get; set; }

    public decimal Salary { get; set; }

    public bool IsActive { get; set; } = true;
}

public record AccountingRequest(
    string TransactionType,
    decimal Amount,
    string Description,
    DateTime TransactionDate,
    string Category,
    string? ReferenceNumber
);

public record CashRequest(
    string TransactionType,
    decimal Amount,
    string Description,
    DateTime TransactionDate,
    string? ReferenceNumber
);

public sealed class ZReportRequest
{
    public DateTime ReportDate { get; set; }
    public decimal GrossSales { get; set; }
    public decimal TotalVat { get; set; }
    public decimal NetSales { get; set; }
    public decimal CashAmount { get; set; }
    public decimal CardAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public int OksFisleriCount { get; set; }
    public decimal KdvToplami { get; set; }
    public decimal SatisToplami { get; set; }
    public decimal Nakit { get; set; }
    public decimal Kredi { get; set; }
    public decimal Diger { get; set; }
    public int IptalCount { get; set; }
    public decimal IptalKdvToplami { get; set; }
    public decimal IptalSatisToplami { get; set; }
    public string? DocumentPath { get; set; }
    public string? OcrRawText { get; set; }
    public bool IsConfirmed { get; set; }
}

public record LeaveRequest(
    int EmployeeId,
    DateOnly LeaveStartDate,
    DateOnly LeaveEndDate,
    string LeaveType,
    string Description
);

public record LeaveRecordResponse(
    int Id,
    int EmployeeId,
    DateOnly LeaveStartDate,
    DateOnly LeaveEndDate,
    int NumberOfDays,
    string LeaveType,
    string Description,
    DateTime CreatedAt
);

public record LeaveStatusResponse(
    int EmployeeId,
    int AnnualEntitlementDays,
    int UsedDays,
    int RemainingDays,
    IReadOnlyList<LeaveRecordResponse> LeaveHistory,
    IReadOnlyList<MonthlyLeaveUsageResponse> MonthlyUsage
);

public record MonthlyLeaveUsageResponse(int Year, int Month, int Days);

public record UserResponse(
    string Id,
    string Email,
    string UserName,
    string FirstName,
    string LastName,
    string Role,
    bool IsActive,
    DateTime CreatedAt
);

public record CreateUserRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string Role
);

public record UpdateUserRequest(
    string FirstName,
    string LastName,
    string Role,
    bool IsActive
);

public record AccountingSummary(decimal TotalIncome, decimal TotalExpense, decimal NetBalance);

public record CashSummary(decimal TotalCashIn, decimal TotalCashOut, decimal CurrentBalance);

public record OcrResult(
    string OcrRawText,
    decimal? GrossSales,
    decimal? TotalVat,
    decimal? NetSales,
    decimal? CashAmount,
    decimal? CardAmount,
    decimal? TotalAmount,
    string? DocumentPath = null,
    int? OksFisleriCount = null,
    decimal? KdvToplami = null,
    decimal? SatisToplami = null,
    decimal? Diger = null,
    int? IptalCount = null,
    decimal? IptalKdvToplami = null,
    decimal? IptalSatisToplami = null,
    decimal? Nakit = null,
    decimal? Kredi = null
);

public record DashboardSummary(
    int TotalCustomers,
    int TotalEmployees,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal NetBalance,
    decimal TotalCashIn,
    decimal TotalCashOut,
    decimal CurrentCashBalance,
    object RecentAccountingTransactions,
    object RecentCashTransactions,
    object RecentZReports
);
