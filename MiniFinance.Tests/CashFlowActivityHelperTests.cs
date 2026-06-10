using MiniFinance.Data.Models;
using MiniFinance.Services;

namespace MiniFinance.Tests;

public class CashFlowActivityHelperTests
{
    [Theory]
    [InlineData("Доход от услуг", "Оплата CRM", CashFlowActivity.Operating)]
    [InlineData("Зарплата", "ФОТ март", CashFlowActivity.Operating)]
    [InlineData("Закупки", "Закупка ноутбука", CashFlowActivity.Investment)]
    [InlineData("Офисные расходы", "Сервер Dell", CashFlowActivity.Investment)]
    [InlineData("Прочее", "Погашение кредита", CashFlowActivity.Financing)]
    [InlineData("Доход от услуг", "Поступление займа", CashFlowActivity.Financing)]
    public void Classify_uses_category_and_description(string category, string description, CashFlowActivity expected)
    {
        var tx = new Transaction { Category = category, Description = description, Amount = -100m };
        Assert.Equal(expected, CashFlowActivityHelper.Classify(tx));
    }

    [Fact]
    public void GetCashFlowStatement_includes_all_transactions()
    {
        var service = new ReportService();
        var start = new DateTime(2026, 1, 1);
        var end = new DateTime(2026, 1, 31);
        var tx = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 1, 5), Amount = 5000m, Category = "Доход от услуг", Description = "Оплата" },
            new() { Date = new DateTime(2026, 1, 8), Amount = -1200m, Category = "Аренда", Description = "Офис" },
            new() { Date = new DateTime(2026, 1, 10), Amount = -3000m, Category = "Закупки", Description = "Ноутбук" },
            new() { Date = new DateTime(2026, 1, 12), Amount = 10000m, Category = "Прочее", Description = "Займ от учредителя" }
        };

        var report = service.GetCashFlowStatement(tx, start, end);

        Assert.Equal(4, report.TransactionCount);
        Assert.Equal(5000m, report.OperatingIncome);
        Assert.Equal(1200m, report.OperatingExpenses);
        Assert.Equal(3000m, report.InvestmentExpenses);
        Assert.Equal(10000m, report.FinancingIncome);
        Assert.Equal(5000m + 10000m - 1200m - 3000m, report.NetCashFlow);
    }
}
