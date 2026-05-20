namespace MiniFinance.Services;

public interface IBankPdfStatementParser
{
    Task<BankStatementImportResult> ParseAsync(
        Stream pdfStream,
        string userId,
        ISet<string>? existingHashes = null,
        CancellationToken cancellationToken = default);
}
