using CustomerBusinessManagement.DataAccess;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerBusinessManagement.DataAccess.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261006120000_BackfillRegistrationDates")]
public sealed class BackfillRegistrationDates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE [Customers] SET [CustomerRegistrationDate] = CONVERT(date, [CreatedAt]) WHERE [CustomerRegistrationDate] = '0001-01-01'");
        migrationBuilder.Sql("UPDATE [Employees] SET [EmployeeRegistrationDate] = CONVERT(date, [CreatedAt]) WHERE [EmployeeRegistrationDate] = '0001-01-01'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
