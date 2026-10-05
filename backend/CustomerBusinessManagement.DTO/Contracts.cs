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

public record CustomerRequest(
    [Required] string CustomerCode,
    [Required] string FirstName,
    [Required] string LastName,
    string? CompanyName,
    [Required] string Phone,
    [Required, EmailAddress] string Email,
    [Required] string Address,
    string? TaxNumber,
    bool IsActive = true
);

public record EmployeeRequest(
    [Required] string FirstName,
    [Required] string LastName,
    [Required] string Phone,
    [Required, EmailAddress] string Email,
    [Required] string Position,
    DateOnly HireDate,
    decimal Salary,
    bool IsActive = true
);

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

public record ZReportRequest(
    DateTime ReportDate,
    decimal GrossSales,
    decimal TotalVat,
    decimal NetSales,
    decimal CashAmount,
    decimal CardAmount,
    decimal TotalAmount,
    string? DocumentPath,
    string? OcrRawText,
    bool IsConfirmed
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
    string? DocumentPath = null
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
