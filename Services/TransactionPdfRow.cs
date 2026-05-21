namespace MiniFinance.Services;

public sealed record TransactionPdfRow(
    DateTime Date,
    string Description,
    string Category,
    decimal Amount,
    string? Project,
    string? Counterparty);
