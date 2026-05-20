namespace MiniFinance.Services;

public interface IReportPdfService
{
    byte[] GenerateProfitLossPdf(ProfitLossReport report, DateTime start, DateTime end);
    byte[] GenerateCashFlowPdf(CashFlowStatementReport report, DateTime start, DateTime end);
    byte[] GenerateFullReportPdf(ReportAnalyticsSnapshot snapshot);
}
