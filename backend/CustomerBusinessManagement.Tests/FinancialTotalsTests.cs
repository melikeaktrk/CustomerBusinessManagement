using CustomerBusinessManagement.Business;
using CustomerBusinessManagement.Entities;

namespace CustomerBusinessManagement.Tests;

public class FinancialTotalsTests
{
    [Fact]
    public void AccountingSummaryTotalsIncomeExpenseAndExcludesCancelledRows()
    {
        var result = FinancialTotals.Accounting(
            new[]
            {
                new AccountingTransaction
                {
                    TransactionType = AccountingType.Income,
                    Amount = 100.25m,
                },
                new AccountingTransaction
                {
                    TransactionType = AccountingType.Expense,
                    Amount = 30m,
                },
                new AccountingTransaction
                {
                    TransactionType = AccountingType.Income,
                    Amount = 500m,
                    IsCancelled = true,
                },
            }
        );
        Assert.Equal(100.25m, result.TotalIncome);
        Assert.Equal(30m, result.TotalExpense);
        Assert.Equal(70.25m, result.NetBalance);
    }

    [Fact]
    public void CashSummaryCalculatesCurrentBalanceAndExcludesCancelledRows()
    {
        var result = FinancialTotals.Cash(
            new[]
            {
                new CashTransaction { TransactionType = CashType.CashIn, Amount = 200m },
                new CashTransaction { TransactionType = CashType.CashOut, Amount = 75m },
                new CashTransaction
                {
                    TransactionType = CashType.CashOut,
                    Amount = 10m,
                    IsCancelled = true,
                },
            }
        );
        Assert.Equal(200m, result.TotalCashIn);
        Assert.Equal(75m, result.TotalCashOut);
        Assert.Equal(125m, result.CurrentBalance);
    }
}
