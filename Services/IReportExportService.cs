namespace MiniFinance.Services;

public interface IReportExportService
{
    byte[] ExportCsv(ReportAnalyticsSnapshot snapshot, string userId);
    byte[] ExportXlsx(ReportAnalyticsSnapshot snapshot, string userId);
    byte[] ExportPdf(ReportAnalyticsSnapshot snapshot, string userId);
    (string ContentType, string FileName, byte[] Data) Export(ReportAnalyticsSnapshot snapshot, string format, string userId);
}
