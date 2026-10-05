using CustomerBusinessManagement.API.Controllers;
using CustomerBusinessManagement.Business;
using CustomerBusinessManagement.DataAccess;
using CustomerBusinessManagement.DTO;
using CustomerBusinessManagement.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CustomerBusinessManagement.Tests;

public class ApiModuleTests
{
    private static ApplicationDbContext CreateDb() =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    [Fact]
    public async Task CustomerCrudCreatesReadsUpdatesAndDeactivatesRecord()
    {
        await using var db = CreateDb();
        var controller = new CustomersController(db, new NoOpAuditLog());
        var created = Assert.IsType<CreatedAtActionResult>(
            await controller.Create(
                new CustomerRequest(
                    "C-001",
                    "Ada",
                    "Yilmaz",
                    null,
                    "555",
                    "ada@example.com",
                    "Istanbul",
                    null
                )
            )
        );
        var customer = Assert.IsType<Customer>(created.Value);
        Assert.IsType<OkObjectResult>(await controller.Get(customer.Id));
        Assert.IsType<NoContentResult>(
            await controller.Update(
                customer.Id,
                new CustomerRequest(
                    "C-001",
                    "Ada",
                    "Kaya",
                    null,
                    "555",
                    "ada@example.com",
                    "Istanbul",
                    null
                )
            )
        );
        Assert.IsType<NoContentResult>(await controller.Delete(customer.Id));
        Assert.False((await db.Customers.SingleAsync()).IsActive);
        var customerList = Assert.IsType<OkObjectResult>(await controller.List(null));
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<Customer>>(customerList.Value));
    }

    [Fact]
    public async Task EmployeeCrudCreatesReadsUpdatesAndDeactivatesRecord()
    {
        await using var db = CreateDb();
        var controller = new EmployeesController(db, new NoOpAuditLog());
        var created = Assert.IsType<CreatedAtActionResult>(
            await controller.Create(
                new EmployeeRequest(
                    "Ali",
                    "Demir",
                    "555",
                    "ali@example.com",
                    "Clerk",
                    new DateOnly(2024, 1, 1),
                    25000m
                )
            )
        );
        var employee = Assert.IsType<Employee>(created.Value);
        Assert.IsType<OkObjectResult>(await controller.Get(employee.Id));
        Assert.IsType<NoContentResult>(
            await controller.Update(
                employee.Id,
                new EmployeeRequest(
                    "Ali",
                    "Demir",
                    "555",
                    "ali@example.com",
                    "Manager",
                    new DateOnly(2024, 1, 1),
                    30000m
                )
            )
        );
        Assert.IsType<NoContentResult>(await controller.Delete(employee.Id));
        Assert.False((await db.Employees.SingleAsync()).IsActive);
        var employeeList = Assert.IsType<OkObjectResult>(await controller.List());
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<Employee>>(employeeList.Value));
    }

    [Fact]
    public async Task AccountingControllerReturnsIncomeExpenseAndNetBalance()
    {
        await using var db = CreateDb();
        var controller = new AccountingTransactionsController(
            db,
            new ManagementService(db),
            new NoOpAuditLog()
        );
        await controller.Create(
            new AccountingRequest("Income", 1200m, "Sale", DateTime.UtcNow, "Sales", null)
        );
        await controller.Create(
            new AccountingRequest("Expense", 250m, "Supplies", DateTime.UtcNow, "Office", null)
        );
        var result = Assert.IsType<OkObjectResult>(await controller.Summary());
        var summary = Assert.IsType<AccountingSummary>(result.Value);
        Assert.Equal(1200m, summary.TotalIncome);
        Assert.Equal(250m, summary.TotalExpense);
        Assert.Equal(950m, summary.NetBalance);
    }

    [Fact]
    public async Task CashControllerReturnsCorrectCurrentBalance()
    {
        await using var db = CreateDb();
        var controller = new CashTransactionsController(
            db,
            new ManagementService(db),
            new NoOpAuditLog()
        );
        await controller.Create(new CashRequest("CashIn", 700m, "Opening", DateTime.UtcNow, null));
        await controller.Create(
            new CashRequest("CashOut", 125m, "Purchase", DateTime.UtcNow, null)
        );
        var result = Assert.IsType<OkObjectResult>(await controller.Summary());
        Assert.Equal(575m, Assert.IsType<CashSummary>(result.Value).CurrentBalance);
    }

    [Fact]
    public async Task ZReportMustBeConfirmedBeforeItCanBeCreated()
    {
        await using var db = CreateDb();
        var controller = new ZReportsController(db, CreateOcrService(), new NoOpAuditLog());
        var unconfirmed = new ZReportRequest(
            DateTime.UtcNow,
            100m,
            10m,
            90m,
            60m,
            40m,
            100m,
            null,
            "manual",
            false
        );
        Assert.IsType<BadRequestObjectResult>(await controller.Create(unconfirmed));
        var confirmed = await controller.Create(unconfirmed with { IsConfirmed = true });
        Assert.IsType<CreatedAtActionResult>(confirmed);
        Assert.Single(await db.ZReports.ToListAsync());
    }

    [Fact]
    public async Task OcrDoesNotInventAmountsWhenTheProviderIsMissing()
    {
        var service = new ZReportOcrService(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Ocr:TesseractPath"] = "missing-tesseract-test" }
        ).Build());
        var file = new Microsoft.AspNetCore.Http.FormFile(
            new MemoryStream([0x00, 0x01, 0x02]),
            0,
            3,
            "file",
            "report.png"
        );

        await Assert.ThrowsAsync<OcrProviderUnavailableException>(() => service.ReadAsync(file, CancellationToken.None));
    }

    [Fact]
    public void OcrParserReadsTurkishCurrencyAndLeavesUnknownValuesNull()
    {
        var result = ZReportOcrParser.Parse(
            "Brüt\nSatış: ₺21.321,50 TL\nToplam KDV: 1.234,50\nNet satış: 20.087,00\nNakit: 12.000,00 TL\nKart: 9.321,50"
        );

        Assert.Equal(21321.50m, result.GrossSales);
        Assert.Equal(1234.50m, result.TotalVat);
        Assert.Equal(20087.00m, result.NetSales);
        Assert.Equal(12000.00m, result.CashAmount);
        Assert.Equal(9321.50m, result.CardAmount);
        Assert.Equal(21321.50m, result.TotalAmount);
        Assert.Null(ZReportOcrParser.Parse("Bu metinde tutar etiketi bulunmuyor.").GrossSales);
    }

    [Fact]
    public async Task DashboardSummaryIncludesCountsAndFinancialTotals()
    {
        await using var db = CreateDb();
        db.Customers.Add(
            new Customer
            {
                CustomerCode = "D-001",
                FirstName = "Ada",
                LastName = "Test",
                Phone = "555",
                Email = "ada@example.com",
                Address = "Istanbul",
            }
        );
        db.Employees.Add(
            new Employee
            {
                FirstName = "Ali",
                LastName = "Test",
                Phone = "555",
                Email = "ali@example.com",
                Position = "Clerk",
                HireDate = new DateOnly(2024, 1, 1),
            }
        );
        db.AccountingTransactions.Add(
            new AccountingTransaction
            {
                TransactionType = AccountingType.Income,
                Amount = 50m,
                Description = "Sale",
                Category = "Sales",
                TransactionDate = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();
        var controller = new DashboardController(db, new ManagementService(db));
        var result = Assert.IsType<OkObjectResult>(await controller.Summary());
        var summary = Assert.IsType<DashboardSummary>(result.Value);
        Assert.Equal(1, summary.TotalCustomers);
        Assert.Equal(1, summary.TotalEmployees);
        Assert.Equal(50m, summary.TotalIncome);
    }

    private sealed class NoOpAuditLog : IAuditLogService
    {
        public Task RecordAsync(
            string action,
            string entityName,
            string entityId,
            string description,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }

    private static ZReportOcrService CreateOcrService() => new(new ConfigurationBuilder().Build());
}
