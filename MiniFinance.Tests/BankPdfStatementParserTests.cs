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

    [Fact]
    public void Parse_multi_tail_line_splits_and_skips_zero_amount()
    {
        var line =
            "41** 29.12.2025 17:21 29.12.2025 22:02 Плата за перевод средств с КС на КС BYN Приход 0.00 0.00 " +
            "41** 29.12.2025 17:21 29.12.2025 22:03 Оплата товаров и услуг MALINOVKA/ SHOP MINSK BY BYN Расход 3.42 3.42 6012";

        var txs = BankPdfStatementParser.ParseTransactionsFromLine(line, 1);
        Assert.Single(txs);
        Assert.Equal(3.42m, txs[0].AccountAmount);
        Assert.Contains("MALINOVKA", txs[0].MerchantPlace ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizeMerchant_removes_time_and_card_noise()
    {
        var m = BankPdfStatementParser.SanitizeMerchant("00:34 карты Оплата товаров MALINOVKA/ SHOP");
        Assert.NotNull(m);
        Assert.Contains("MALINOVKA", m, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("00:34", m);
        Assert.DoesNotContain("карты", m, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeCardNumber_collapses_repeated_bins()
    {
        var card = BankImportTextHelper.NormalizeCardNumber("4246 4246 4246 41** 41** **** 3669 3669");
        Assert.Equal("•••• 3669", card);
    }

    [Fact]
    public void BuildImportDescription_is_short_and_clean()
    {
        var desc = BankImportTextHelper.BuildImportDescription(
            "Оплата товаров и услуг", "MALINOVKA/ SHOP MINSK");
        Assert.StartsWith("Оплата —", desc);
        Assert.Contains("MALINOVKA", desc, StringComparison.OrdinalIgnoreCase);
        Assert.True(desc.Length < 100);
    }
}
