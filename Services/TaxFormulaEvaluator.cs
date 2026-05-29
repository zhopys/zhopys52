using System.Globalization;
using System.Text.RegularExpressions;

namespace MiniFinance.Services;

public sealed class TaxFormulaContext
{
    public decimal Income { get; init; }
    public decimal Expenses { get; init; }
    public decimal Profit => Income - Expenses;
}

public static class TaxFormulaEvaluator
{
    private static readonly Regex VarRegex = new(@"\b(income|expenses|profit)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static (bool Ok, decimal Value, string? Error) TryEvaluate(string formula, TaxFormulaContext ctx)
    {
        if (string.IsNullOrWhiteSpace(formula))
            return (false, 0, "Формула пустая");

        try
        {
            var prepared = PrepareFormula(formula.Trim(), ctx);
            var value = EvaluatePrepared(prepared);
            if (value < 0)
                return (false, 0, "Результат формулы не может быть отрицательным");
            return (true, Math.Round(value, 2), null);
        }
        catch (Exception ex)
        {
            return (false, 0, TranslateFormulaError(ex));
        }
    }

    private static string TranslateFormulaError(Exception ex) => ex switch
    {
        DivideByZeroException => "Деление на ноль в формуле",
        OverflowException => "Число в формуле слишком большое",
        InvalidOperationException op when !string.IsNullOrWhiteSpace(op.Message) => op.Message,
        _ => "Ошибка в формуле — проверьте синтаксис и переменные income, expenses, profit"
    };

    private static string PrepareFormula(string formula, TaxFormulaContext ctx)
    {
        var s = formula.Replace(',', '.');
        s = Regex.Replace(s, @"\bmax\s*\(", "MAX(", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bmin\s*\(", "MIN(", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bround\s*\(", "ROUND(", RegexOptions.IgnoreCase);

        while (true)
        {
            var m = Regex.Match(s, @"\b(MAX|MIN|ROUND)\s*\(([^()]+)\)", RegexOptions.IgnoreCase);
            if (!m.Success) break;

            var fn = m.Groups[1].Value.ToUpperInvariant();
            var args = SplitArgs(m.Groups[2].Value);
            if (args.Count == 0)
                throw new InvalidOperationException($"Неверный вызов {fn}");

            decimal inner;
            if (fn == "ROUND")
            {
                if (args.Count != 1 && args.Count != 2)
                    throw new InvalidOperationException("round(x) или round(x, digits)");
                inner = EvaluatePrepared(args[0]);
                var digits = args.Count == 2 ? (int)EvaluatePrepared(args[1]) : 0;
                inner = Math.Round(inner, Math.Clamp(digits, 0, 6));
            }
            else
            {
                if (args.Count != 2)
                    throw new InvalidOperationException($"{fn} требует два аргумента");
                var a = EvaluatePrepared(args[0]);
                var b = EvaluatePrepared(args[1]);
                inner = fn == "MAX" ? Math.Max(a, b) : Math.Min(a, b);
            }

            s = s[..m.Index] + inner.ToString(CultureInfo.InvariantCulture) + s[(m.Index + m.Length)..];
        }

        s = VarRegex.Replace(s, m =>
        {
            var v = m.Value.ToLowerInvariant() switch
            {
                "income" => ctx.Income,
                "expenses" => ctx.Expenses,
                "profit" => ctx.Profit,
                _ => 0m
            };
            return v.ToString(CultureInfo.InvariantCulture);
        });

        return s;
    }

    private static List<string> SplitArgs(string inner)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(inner[start..i].Trim());
                start = i + 1;
            }
        }
        parts.Add(inner[start..].Trim());
        return parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
    }

    private static decimal EvaluatePrepared(string expr)
    {
        expr = expr.Trim();
        if (string.IsNullOrEmpty(expr))
            throw new InvalidOperationException("Пустое выражение");

        var table = new System.Data.DataTable { Locale = CultureInfo.InvariantCulture };
        var obj = table.Compute(expr, null);
        return Convert.ToDecimal(obj, CultureInfo.InvariantCulture);
    }
}
