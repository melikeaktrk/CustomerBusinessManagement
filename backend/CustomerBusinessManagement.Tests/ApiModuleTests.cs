using CustomerBusinessManagement.API.Controllers;
using CustomerBusinessManagement.Business;
using CustomerBusinessManagement.DataAccess;
using CustomerBusinessManagement.DTO;
using CustomerBusinessManagement.Entities;
using System.Globalization;
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
                new CustomerRequest
                {
                    CustomerCode = "C-001",
                    FirstName = "Ada",
                    LastName = "Yilmaz",
                    Phone = "555",
                    Email = "ada@example.com",
                    Address = "Istanbul",
                }
            )
        );
        var customer = Assert.IsType<Customer>(created.Value);
        Assert.IsType<OkObjectResult>(await controller.Get(customer.Id));
        Assert.IsType<NoContentResult>(
            await controller.Update(
                customer.Id,
                new CustomerRequest
                {
                    CustomerCode = "C-001",
                    FirstName = "Ada",
                    LastName = "Kaya",
                    Phone = "555",
                    Email = "ada@example.com",
                    Address = "Istanbul",
                }
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
                new EmployeeRequest
                {
                    FirstName = "Ali",
                    LastName = "Demir",
                    Phone = "555",
                    Email = "ali@example.com",
                    Position = "Clerk",
                    HireDate = new DateOnly(2024, 1, 1),
                    Salary = 25000m,
                }
            )
        );
        var employee = Assert.IsType<Employee>(created.Value);
        Assert.IsType<OkObjectResult>(await controller.Get(employee.Id));
        Assert.IsType<NoContentResult>(
            await controller.Update(
                employee.Id,
                new EmployeeRequest
                {
                    FirstName = "Ali",
                    LastName = "Demir",
                    Phone = "555",
                    Email = "ali@example.com",
                    Position = "Manager",
                    HireDate = new DateOnly(2024, 1, 1),
                    Salary = 30000m,
                }
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
        var unconfirmed = new ZReportRequest
        {
            ReportDate = DateTime.UtcNow,
            GrossSales = 100m,
            TotalVat = 10m,
            NetSales = 90m,
            CashAmount = 60m,
            CardAmount = 40m,
            TotalAmount = 100m,
            OcrRawText = "manual",
        };
        Assert.IsType<BadRequestObjectResult>(await controller.Create(unconfirmed));
        unconfirmed.IsConfirmed = true;
        var confirmed = await controller.Create(unconfirmed);
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
            "TOPLAM CİRO:\n₺47.508,25 TL\nKDV TUTARI: 7.600,00 TL\nNet satış: 39.908,25\nNakit: 28.000,00 TL\nKart: 19.508,25"
        );

        Assert.Equal(47508.25m, result.GrossSales);
        Assert.Equal(7600.00m, result.TotalVat);
        Assert.Equal(39908.25m, result.NetSales);
        Assert.Equal(28000.00m, result.CashAmount);
        Assert.Equal(19508.25m, result.CardAmount);
        Assert.Equal(47508.25m, result.TotalAmount);
        Assert.Null(ZReportOcrParser.Parse("Bu metinde tutar etiketi bulunmuyor.").GrossSales);
    }

    [Fact]
    public void OcrParserReadsGrossSalesAndVatFromLiveTurkishOcrText()
    {
        var result = ZReportOcrParser.Parse("Toplam Ciro 1.250,50\nKDV Tutarı 200,08");
        var uppercaseWithLineBreaks = ZReportOcrParser.Parse(
            "TOPLAM CİRO-\n800,00\nKDV TUTARI\n-\n80,00"
        );

        Assert.Equal(1250.50m, result.GrossSales);
        Assert.Equal(200.08m, result.TotalVat);
        Assert.Equal(800.00m, uppercaseWithLineBreaks.GrossSales);
        Assert.Equal(80.00m, uppercaseWithLineBreaks.TotalVat);
    }

    [Fact]
    public void OcrParserExtractsReceiptDocumentTypeTotals()
    {
        var result = ZReportOcrParser.Parse(
            "BELGE TEPLERT OKC FİŞLERİ 99 -KOV TOPLAMI *1 067,82 -SATIŞ TOPLAMI *11.745,00 -NAKİT *2 355,00 -KREDİ *9 390,00 -DİĞER *0,00 İPTAL 3 -KOV TOPLAMI *8,64 -SATIŞ TOPLAMI *95,00 SAYACLAR"
        );

        Assert.Equal(99, result.OksFisleriCount);
        Assert.Equal(1067.82m, result.KdvToplami);
        Assert.Equal(11745.00m, result.SatisToplami);
        Assert.Equal(2355.00m, result.Nakit);
        Assert.Equal(9390.00m, result.Kredi);
        Assert.Equal(0m, result.Diger);
        Assert.Equal(3, result.IptalCount);
        Assert.Equal(8.64m, result.IptalKdvToplami);
        Assert.Equal(95.00m, result.IptalSatisToplami);
    }

    [Fact]
    public void OcrParserExtractsDocumentTypeValuesFromReceiptLayout()
    {
        var result = ZReportOcrParser.Parse(
            """
            BELGE TİPLERİ
            ÖKC FİŞLERİ 88
            -KDV TOPLAMI *1.989,98
            -SATIŞ TOPLAMI *21.890,00
            -NAKİT *4.425,00
            -KREDİ *17.465,00
            -DİĞER *0,00
            İPTAL 10
            -KDV TOPLAMI *194,07
            -SATIŞ TOPLAMI *2.135,00
            SAYAÇLAR
            """
        );

        Assert.Equal(88, result.OksFisleriCount);
        Assert.Equal(1989.98m, result.KdvToplami);
        Assert.Equal(21890.00m, result.SatisToplami);
        Assert.Equal(4425.00m, result.Nakit);
        Assert.Equal(17465.00m, result.Kredi);
        Assert.Equal(0m, result.Diger);
        Assert.Equal(10, result.IptalCount);
        Assert.Equal(194.07m, result.IptalKdvToplami);
        Assert.Equal(2135.00m, result.IptalSatisToplami);
    }

    [Theory]
    [InlineData(98, 1509.42, 16603, 5530, 11073, 0, 3, 70.91, 780)]
    [InlineData(88, 1989.98, 21890, 4425, 17465, 0, 10, 194.07, 2135)]
    [InlineData(99, 1067.82, 11745, 2355, 9390, 0, 3, 8.64, 95)]
    [InlineData(70, 944.16, 10385, 1490, 8895, 0, 2, 60.46, 665)]
    public void OcrParserReadsDocumentTypesForReferenceReceipts(
        int receiptCount,
        decimal vat,
        decimal sales,
        decimal cash,
        decimal credit,
        decimal other,
        int cancelledCount,
        decimal cancelledVat,
        decimal cancelledSales)
    {
        var result = ZReportOcrParser.Parse($"""
            GÜNLÜK FİŞ DÖKÜMÜ
            BELGE TİPLERİ
            ÖKC FİŞLERİ {receiptCount}
            -KDV TOPLAMI *{vat.ToString("0.00", CultureInfo.InvariantCulture)}
            -SATIŞ TOPLAMI *{sales.ToString("0.00", CultureInfo.InvariantCulture)}
            -NAKİT *{cash.ToString("0.00", CultureInfo.InvariantCulture)}
            -KREDİ *{credit.ToString("0.00", CultureInfo.InvariantCulture)}
            -DİĞER *{other.ToString("0.00", CultureInfo.InvariantCulture)}
            İPTAL {cancelledCount}
            -KDV TOPLAMI *{cancelledVat.ToString("0.00", CultureInfo.InvariantCulture)}
            -SATIŞ TOPLAMI *{cancelledSales.ToString("0.00", CultureInfo.InvariantCulture)}
            SAYAÇLAR
            """);

        Assert.Equal(receiptCount, result.OksFisleriCount);
        Assert.Equal(vat, result.KdvToplami);
        Assert.Equal(sales, result.SatisToplami);
        Assert.Equal(cash, result.Nakit);
        Assert.Equal(credit, result.Kredi);
        Assert.Equal(other, result.Diger);
        Assert.Equal(cancelledCount, result.IptalCount);
        Assert.Equal(cancelledVat, result.IptalKdvToplami);
        Assert.Equal(cancelledSales, result.IptalSatisToplami);
    }

    [Fact]
    public void OcrParserUsesPaymentAndCounterCrossChecksForNoisyFullReceipt()
    {
        var result = ZReportOcrParser.ParseBest(
        [
            """
            GÜNLÜK FİŞ DÖKÜMÜ
            TOPLAM *10.385,00
            ÖDEME BİLGİLERİ
            NAKİT 21
            TOPLAM *1.490,00
            KREDİ 39
            TOPLAM *6.835,00
            KAREKOD KART 10
            TOPLAM *2.060,00
            BELGE TİPLERİ
            ÖKC FİŞLERİ 70
            -KDV TOPLAMI *944,16
            -SATIŞ TOPLAMI *10.385,00
            -NAKİT *1.490,00
            -KREDİ *8.895,00
            -DİĞER *0,00
            İPTAL 2
            -KDV TOPLAMI *60,46
            -SATIŞ TOPLAMI *665,00
            SAYAÇLAR
            MÜŞTERİ FİŞİ ADETİ 70
            SATIŞ İPTAL 2
            SATIŞ İPTAL TUTAR *665,00
            """
        ]);

        Assert.Equal(70, result.OksFisleriCount);
        Assert.Equal(8895m, result.Kredi);
        Assert.Equal(0m, result.Diger);
        Assert.Equal(665m, result.IptalSatisToplami);
    }

    [Fact]
    public void OcrParserPrefersFullPassCountersOverCropWithoutCounters()
    {
        var result = ZReportOcrParser.ParseBest(
        [
            """
            BELGE TİPLERİ
            ÖKC FİŞLERİ 10
            -SATIŞ TOPLAMI *10.385,00
            -NAKİT *1.490,00
            -KREDİ *8.895,00
            -DİĞER *0,00
            İPTAL 2
            -SATIŞ TOPLAMI *665,00
            SAYAÇLAR
            """,
            """
            GÜNLÜK FİŞ DÖKÜMÜ
            TOPLAM *10.385,00
            ÖDEME BİLGİLERİ
            NAKİT
            TOPLAM *1.490,00
            KREDİ
            TOPLAM *8.895,00
            BELGE TİPLERİ
            ÖKC FİŞLERİ 70
            -KDV TOPLAMI *944,16
            -SATIŞ TOPLAMI *10.385,00
            -NAKİT *1.490,00
            -KREDİ *8.895,00
            -DİĞER *0,00
            İPTAL 2
            -KDV TOPLAMI *60,46
            -SATIŞ TOPLAMI *665,00
            SAYAÇLAR
            MÜŞTERİ FİŞİ ADETİ 70
            SATIŞ İPTAL TUTAR *665,00
            """
        ]);

        Assert.Equal(70, result.OksFisleriCount);
        Assert.Equal(665m, result.IptalSatisToplami);
    }

    [Fact]
    public void OcrParserDerivesReceiptCountFromPaymentCountsWhenLabelIsUnreadable()
    {
        var result = ZReportOcrParser.Parse(
            """
            ÖDEME BİLGİLERİ
            NAKİT 21
            TOPLAM *1.490,00
            KREDİ 39
            TOPLAM *6.835,00
            KAREKOD KART 10
            TOPLAM *2.060,00
            BELGE TİPLERİ
            ÖKC FİŞLERİ
            -SATIŞ TOPLAMI *10.385,00
            -NAKİT *1.490,00
            -KREDİ *8.895,00
            -DİĞER *0,00
            SAYAÇLAR
            """
        );

        Assert.Equal(70, result.OksFisleriCount);
    }

    [Fact]
    public async Task LeaveRequiresCompletedWorkYearAndCountsWeekdaysAcrossMonths()
    {
        await using var db = CreateDb();
        var employee = new Employee
        {
            FirstName = "Ayşe",
            LastName = "Test",
            Phone = "555",
            Email = "ayse@example.com",
            Position = "Personel",
            HireDate = new DateOnly(2025, 6, 15),
            EmployeeRegistrationDate = new DateOnly(2025, 6, 15),
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        var service = new LeaveService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            new LeaveRequest(employee.Id, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 12), "Yıllık İzin", "Bir gün")
        ));
        var leave = await service.CreateAsync(
            new LeaveRequest(employee.Id, new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 19), "Yıllık İzin", "Bir hafta")
        );
        var status = await service.GetStatusAsync(employee.Id, new DateTime(2026, 6, 20));

        Assert.Equal(5, leave.NumberOfDays);
        Assert.Equal(14, status.AnnualEntitlementDays);
        Assert.Equal(5, status.UsedDays);
        Assert.Equal(9, status.RemainingDays);
        Assert.Contains(status.MonthlyUsage, month => month.Year == 2026 && month.Month == 6 && month.Days == 5);
    }

    [Fact]
    public async Task LeaveBalanceRecalculatesOldRecordsFromTheirDateRanges()
    {
        await using var db = CreateDb();
        var employee = new Employee
        {
            FirstName = "Deniz",
            LastName = "Test",
            Phone = "555",
            Email = "deniz@example.com",
            Position = "Personel",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeRegistrationDate = new DateOnly(2020, 1, 1),
        };
        db.Employees.Add(employee);
        db.EmployeeLeaveRecords.Add(new EmployeeLeaveRecord
        {
            Employee = employee,
            LeaveStartDate = new DateOnly(2026, 6, 15),
            LeaveEndDate = new DateOnly(2026, 6, 19),
            NumberOfDays = 1,
            LeaveYear = 2026,
            Description = "Eski izin",
        });
        await db.SaveChangesAsync();

        var status = await new LeaveService(db).GetStatusAsync(employee.Id, new DateTime(2026, 6, 20));

        Assert.Equal(20, status.AnnualEntitlementDays);
        Assert.Equal(5, status.UsedDays);
        Assert.Equal(15, status.RemainingDays);
        Assert.Equal(5, Assert.Single(status.MonthlyUsage).Days);
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
