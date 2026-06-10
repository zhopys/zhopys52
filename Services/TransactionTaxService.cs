using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public sealed class TransactionTaxService : ITransactionTaxService
{
    private static readonly string[] TransferKeywords = ["перевод", "трансфер", "между счет", "внутрен"];
    private static readonly string[] LegalEntityKeywords = ["ооо", "зао", "оао", "уп ", "чуп", "ип ", "предприят"];

    private readonly ApplicationDbContext _db;
    private readonly IDataScopeService _dataScope;

    public TransactionTaxService(ApplicationDbContext db, IDataScopeService dataScope)
    {
        _db = db;
        _dataScope = dataScope;
    }

    public async Task<TaxPeriodAnalysisDto> AnalyzePeriodAsync(
        string ownerUserId,
        DateTime start,
        DateTime end,
        TaxSystem taxSystem,
        TaxpayerKind taxpayerKind,
        bool includeFsznEstimate = false)
    {
        ownerUserId = await ServiceDataScope.ResolveAsync(_dataScope, ownerUserId);
        if (end < start)
            (start, end) = (end, start);

        var transactions = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == ownerUserId && t.IsConfirmed && t.Date >= start && t.Date <= end)
            .OrderBy(t => t.Date).ThenBy(t => t.Id)
            .Select(t => new TxRow(
                t.Id,
                t.Date,
                t.Description,
                t.Category,
                t.Amount,
                t.Counterparty,
                t.CounterpartyId,
                t.CounterpartyEntity != null ? t.CounterpartyEntity.TaxId : null))
            .ToListAsync();

        var lines = new List<TransactionTaxLine>();
        decimal taxableIncome = 0, deductible = 0, fromFl = 0, fromYur = 0, excluded = 0;
        var excludedCount = 0;

        foreach (var tx in transactions)
        {
            var line = ClassifyAndAccrue(tx, taxSystem, taxpayerKind);
            lines.Add(line);

            switch (line.Treatment)
            {
                case TransactionTaxTreatment.TaxableIncome:
                    taxableIncome += line.TaxBase;
                    break;
                case TransactionTaxTreatment.DeductibleExpense:
                    deductible += line.TaxBase;
                    break;
                case TransactionTaxTreatment.NpdFromIndividual:
                    fromFl += line.TaxBase;
                    break;
                case TransactionTaxTreatment.NpdFromLegal:
                    fromYur += line.TaxBase;
                    break;
                case TransactionTaxTreatment.Excluded:
                case TransactionTaxTreatment.TaxPayment:
                    excluded += Math.Abs(tx.Amount);
                    excludedCount++;
                    break;
            }
        }

        var accruedTotal = lines.Sum(l => l.AccruedTax);

        if (taxSystem == TaxSystem.OSN)
        {
            var profit = Math.Max(0, taxableIncome - deductible);
            var rate = taxpayerKind == TaxpayerKind.IndividualEntrepreneur ? 0.16m : 0.20m;
            var profitTax = Math.Round(profit * rate, 2);
            var vatTotal = taxpayerKind == TaxpayerKind.LegalEntity
                ? Math.Round(taxableIncome * 20m / 120m, 2)
                : 0m;

            accruedTotal = profitTax + vatTotal;
            DistributeOsnTax(lines, profitTax, vatTotal, taxableIncome);
            accruedTotal = lines.Sum(l => l.AccruedTax);
        }

        var calc = TaxCalculatorHelper.Calculate(new TaxCalculatorInput
        {
            System = taxSystem,
            TaxpayerKind = taxpayerKind,
            Income = taxSystem == TaxSystem.NPD ? fromFl + fromYur : taxableIncome,
            Expenses = deductible,
            IncomeFromIndividuals = fromFl,
            IncomeFromLegalEntities = fromYur,
            IncludeFsznEstimate = includeFsznEstimate
        });

        return new TaxPeriodAnalysisDto
        {
            PeriodStart = start,
            PeriodEnd = end,
            TaxSystem = taxSystem,
            TaxpayerKind = taxpayerKind,
            TaxableIncome = taxableIncome,
            DeductibleExpenses = deductible,
            IncomeFromIndividuals = fromFl,
            IncomeFromLegalEntities = fromYur,
            ExcludedAmount = excluded,
            OperationCount = transactions.Count,
            ExcludedCount = excludedCount,
            AccruedTaxTotal = taxSystem == TaxSystem.OSN ? accruedTotal : lines.Sum(l => l.AccruedTax),
            Calculation = calc,
            Lines = lines
        };
    }

    public async Task<TransactionTaxLine?> AnalyzeTransactionAsync(
        string ownerUserId,
        int transactionId,
        TaxSystem taxSystem,
        TaxpayerKind taxpayerKind)
    {
        ownerUserId = await ServiceDataScope.ResolveAsync(_dataScope, ownerUserId);
        var tx = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.Id == transactionId && t.UserId == ownerUserId && t.IsConfirmed)
            .Select(t => new TxRow(
                t.Id,
                t.Date,
                t.Description,
                t.Category,
                t.Amount,
                t.Counterparty,
                t.CounterpartyId,
                t.CounterpartyEntity != null ? t.CounterpartyEntity.TaxId : null))
            .FirstOrDefaultAsync();

        return tx == null ? null : ClassifyAndAccrue(tx, taxSystem, taxpayerKind);
    }

    public bool IsExcludedFromTaxBase(string? category, string? description)
    {
        if (CategoryBucketHelper.IsTax(category ?? ""))
            return true;
        var text = $"{category} {description}".ToLowerInvariant();
        return TransferKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsLegalEntityCounterparty(string? counterpartyName, string? taxId)
    {
        if (!string.IsNullOrWhiteSpace(taxId))
            return true;
        if (string.IsNullOrWhiteSpace(counterpartyName))
            return false;
        var name = counterpartyName.ToLowerInvariant();
        return LegalEntityKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private TransactionTaxLine ClassifyAndAccrue(TxRow tx, TaxSystem system, TaxpayerKind kind)
    {
        if (IsExcludedFromTaxBase(tx.Category, tx.Description))
        {
            var isTax = CategoryBucketHelper.IsTax(tx.Category);
            return new TransactionTaxLine
            {
                TransactionId = tx.Id,
                Date = tx.Date,
                Description = tx.Description,
                Category = tx.Category,
                Counterparty = tx.Counterparty,
                Amount = tx.Amount,
                Treatment = isTax ? TransactionTaxTreatment.TaxPayment : TransactionTaxTreatment.Excluded,
                Note = isTax ? "Уплата налога — не входит в базу" : "Исключено из расчёта"
            };
        }

        if (tx.Amount > 0)
            return ClassifyIncome(tx, system, kind);

        return ClassifyExpense(tx, system);
    }

    private TransactionTaxLine ClassifyIncome(TxRow tx, TaxSystem system, TaxpayerKind kind)
    {
        var amount = tx.Amount;

        return system switch
        {
            TaxSystem.NPD => ClassifyNpdIncome(tx, amount),
            TaxSystem.UnifiedTax => new TransactionTaxLine
            {
                TransactionId = tx.Id,
                Date = tx.Date,
                Description = tx.Description,
                Category = tx.Category,
                Counterparty = tx.Counterparty,
                Amount = amount,
                Treatment = TransactionTaxTreatment.Excluded,
                Note = "Единый налог — фиксированная сумма, не от выручки"
            },
            TaxSystem.OSN when kind == TaxpayerKind.LegalEntity => new TransactionTaxLine
            {
                TransactionId = tx.Id,
                Date = tx.Date,
                Description = tx.Description,
                Category = tx.Category,
                Counterparty = tx.Counterparty,
                Amount = amount,
                Treatment = TransactionTaxTreatment.TaxableIncome,
                TaxBase = amount,
                AccruedTax = Math.Round(amount * 20m / 120m, 2),
                RateLabel = "НДС 20/120",
                Note = "Выручка + оценка НДС"
            },
            TaxSystem.OSN => new TransactionTaxLine
            {
                TransactionId = tx.Id,
                Date = tx.Date,
                Description = tx.Description,
                Category = tx.Category,
                Counterparty = tx.Counterparty,
                Amount = amount,
                Treatment = TransactionTaxTreatment.TaxableIncome,
                TaxBase = amount,
                RateLabel = kind == TaxpayerKind.IndividualEntrepreneur ? "16%" : "20%",
                Note = "Доход — участвует в базе подоходного"
            },
            _ => new TransactionTaxLine
            {
                TransactionId = tx.Id,
                Date = tx.Date,
                Description = tx.Description,
                Category = tx.Category,
                Counterparty = tx.Counterparty,
                Amount = amount,
                Treatment = TransactionTaxTreatment.TaxableIncome,
                TaxBase = amount,
                AccruedTax = Math.Round(amount * 0.06m, 2),
                RateLabel = "6%",
                Note = "УСН: 6% от выручки"
            }
        };
    }

    private TransactionTaxLine ClassifyNpdIncome(TxRow tx, decimal amount)
    {
        var isLegal = IsLegalEntityCounterparty(tx.Counterparty, tx.CounterpartyTaxId);
        if (isLegal)
        {
            return new TransactionTaxLine
            {
                TransactionId = tx.Id,
                Date = tx.Date,
                Description = tx.Description,
                Category = tx.Category,
                Counterparty = tx.Counterparty,
                Amount = amount,
                Treatment = TransactionTaxTreatment.NpdFromLegal,
                TaxBase = amount,
                AccruedTax = Math.Round(amount * 0.08m, 2),
                RateLabel = "8%",
                Note = "НПД: доход от юрлица/ИП"
            };
        }

        return new TransactionTaxLine
        {
            TransactionId = tx.Id,
            Date = tx.Date,
            Description = tx.Description,
            Category = tx.Category,
            Counterparty = tx.Counterparty,
            Amount = amount,
            Treatment = TransactionTaxTreatment.NpdFromIndividual,
            TaxBase = amount,
            AccruedTax = Math.Round(amount * 0.04m, 2),
            RateLabel = "4%",
            Note = "НПД: доход от физлица"
        };
    }

    private static TransactionTaxLine ClassifyExpense(TxRow tx, TaxSystem system)
    {
        var amount = Math.Abs(tx.Amount);

        if (system == TaxSystem.OSN)
        {
            return new TransactionTaxLine
            {
                TransactionId = tx.Id,
                Date = tx.Date,
                Description = tx.Description,
                Category = tx.Category,
                Counterparty = tx.Counterparty,
                Amount = tx.Amount,
                Treatment = TransactionTaxTreatment.DeductibleExpense,
                TaxBase = amount,
                Note = "Расход — уменьшает налоговую базу"
            };
        }

        return new TransactionTaxLine
        {
            TransactionId = tx.Id,
            Date = tx.Date,
            Description = tx.Description,
            Category = tx.Category,
            Counterparty = tx.Counterparty,
            Amount = tx.Amount,
            Treatment = TransactionTaxTreatment.Excluded,
            Note = system == TaxSystem.USN ? "Расход не влияет на УСН" : "Расход не учитывается в этом режиме"
        };
    }

    private static void DistributeOsnTax(List<TransactionTaxLine> lines, decimal profitTax, decimal vatTotal, decimal taxableIncome)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Treatment != TransactionTaxTreatment.TaxableIncome)
                continue;

            var vatPart = line.AccruedTax;
            decimal incomeTaxPart = 0;
            if (taxableIncome > 0 && profitTax > 0)
                incomeTaxPart = Math.Round(profitTax * (line.TaxBase / taxableIncome), 2);

            lines[i] = new TransactionTaxLine
            {
                TransactionId = line.TransactionId,
                Date = line.Date,
                Description = line.Description,
                Category = line.Category,
                Counterparty = line.Counterparty,
                Amount = line.Amount,
                Treatment = line.Treatment,
                TaxBase = line.TaxBase,
                RateLabel = line.RateLabel,
                AccruedTax = vatPart + incomeTaxPart,
                Note = vatPart > 0
                    ? $"НДС {vatPart:N2} + подоходный {incomeTaxPart:N2}"
                    : $"Подоходный (доля) {incomeTaxPart:N2}"
            };
        }

        var sum = lines.Sum(l => l.AccruedTax);
        var diff = profitTax + vatTotal - sum;
        if (Math.Abs(diff) >= 0.01m)
        {
            var idx = lines.FindIndex(l => l.Treatment == TransactionTaxTreatment.TaxableIncome);
            if (idx >= 0)
            {
                var line = lines[idx];
                lines[idx] = new TransactionTaxLine
                {
                    TransactionId = line.TransactionId,
                    Date = line.Date,
                    Description = line.Description,
                    Category = line.Category,
                    Counterparty = line.Counterparty,
                    Amount = line.Amount,
                    Treatment = line.Treatment,
                    TaxBase = line.TaxBase,
                    RateLabel = line.RateLabel,
                    AccruedTax = line.AccruedTax + diff,
                    Note = line.Note
                };
            }
        }
    }

    private sealed record TxRow(
        int Id,
        DateTime Date,
        string Description,
        string Category,
        decimal Amount,
        string? Counterparty,
        int? CounterpartyId,
        string? CounterpartyTaxId);
}
