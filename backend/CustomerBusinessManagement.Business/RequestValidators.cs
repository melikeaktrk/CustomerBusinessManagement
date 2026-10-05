using CustomerBusinessManagement.DTO;
using FluentValidation;

namespace CustomerBusinessManagement.Business;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class CustomerRequestValidator : AbstractValidator<CustomerRequest>
{
    public CustomerRequestValidator()
    {
        RuleFor(x => x.CustomerCode).NotEmpty().MaximumLength(40);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
    }
}

public sealed class EmployeeRequestValidator : AbstractValidator<EmployeeRequest>
{
    public EmployeeRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty();
        RuleFor(x => x.LastName).NotEmpty();
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Position).NotEmpty();
        RuleFor(x => x.Salary).GreaterThanOrEqualTo(0);
        RuleFor(x => x.HireDate).NotEqual(default(DateOnly));
    }
}

public sealed class AccountingRequestValidator : AbstractValidator<AccountingRequest>
{
    public AccountingRequestValidator()
    {
        RuleFor(x => x.TransactionType)
            .Must(x => Enum.TryParse<Entities.AccountingType>(x, true, out _));
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Description).NotEmpty();
        RuleFor(x => x.TransactionDate).NotEqual(default(DateTime));
        RuleFor(x => x.Category).NotEmpty();
    }
}

public sealed class CashRequestValidator : AbstractValidator<CashRequest>
{
    public CashRequestValidator()
    {
        RuleFor(x => x.TransactionType).Must(x => Enum.TryParse<Entities.CashType>(x, true, out _));
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Description).NotEmpty();
        RuleFor(x => x.TransactionDate).NotEqual(default(DateTime));
    }
}

public sealed class ZReportRequestValidator : AbstractValidator<ZReportRequest>
{
    public ZReportRequestValidator()
    {
        RuleFor(x => x.ReportDate).NotEqual(default(DateTime));
        RuleFor(x => x.GrossSales).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TotalVat).GreaterThanOrEqualTo(0);
        RuleFor(x => x.NetSales).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CashAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CardAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TotalAmount).GreaterThanOrEqualTo(0);
    }
}
