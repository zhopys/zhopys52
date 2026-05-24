using MiniFinance.Services;
using Xunit;
using Xunit.Abstractions;

namespace MiniFinance.Tests;

public class BankPdfStatementParserTests
{
    private readonly ITestOutputHelper _output;

    public BankPdfStatementParserTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Parse_sample_statement_extracts_transactions()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "bank-statement-sample.pdf");
        if (!File.Exists(path))
            path = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "wwwroot", "samples", "bank-statement-sample.pdf"));

        Assert.True(File.Exists(path), $"Sample PDF not found: {path}");

        var bytes = File.ReadAllBytes(path);
        var lines = BankPdfStatementParser.ExtractLinesFromPdf(bytes);
        _output.WriteLine($"Lines: {lines.Count}");
        foreach (var line in lines.Take(25))
            _output.WriteLine(line);

        var header = BankPdfStatementParser.ParseHeader(lines);
        _output.WriteLine($"IBAN={header.Iban} open={header.OpeningBalance} close={header.ClosingBalance}");

        var errors = new List<CsvImportError>();
        var txs = BankPdfStatementParser.ParseTransactionsStatic(lines, errors);
        _output.WriteLine($"Transactions={txs.Count} errors={errors.Count}");
        foreach (var e in errors.Take(10))
            _output.WriteLine($"ERR L{e.LineNumber}: {e.Message} :: {e.RawLine}");

        Assert.True(txs.Count > 5, $"Expected transactions, got {txs.Count}. Errors: {errors.Count}");
    }
}
