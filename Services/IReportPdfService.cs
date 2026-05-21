namespace MiniFinance.Services;

public interface IReportPdfService
{
    byte[] GenerateProfitLossPdf(ProfitLossReport report, DateTime start, DateTime end);
    byte[] GenerateCashFlowPdf(CashFlowStatementReport report, DateTime start, DateTime end);
    byte[] GenerateTrialBalancePdf(TrialBalanceReport report, DateTime start, DateTime end);
    byte[] GenerateIncomeBookPdf(IReadOnlyList<IncomeExpenseBookEntry> entries, DateTime start, DateTime end);
    byte[] GenerateProfitabilityPdf(IReadOnlyList<ProfitabilityMatrixRowDto> rows, DateTime start, DateTime end);
    byte[] GenerateForecastPdf(CashForecastChartDto forecast, DateTime start, DateTime end);
    byte[] GenerateTransactionsPdf(IReadOnlyList<TransactionPdfRow> rows, DateTime start, DateTime end);
    byte[] GenerateFullReportPdf(ReportAnalyticsSnapshot snapshot, IReadOnlyList<TransactionPdfRow> transactions);
}
