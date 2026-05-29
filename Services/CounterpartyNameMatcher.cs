using System.Text.RegularExpressions;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

/// <summary>Сопоставление названий контрагентов при импорте (без дублей «почти одинаковых»).</summary>
public static class CounterpartyNameMatcher
{
    private static readonly string[] NoiseTokens =
    [
        "MINSK", "MINSKIY", "MINSKIY", "BLR", "BY", "BYN", "USD", "EUR", "RUB",
        "ERIP", "SERVIC", "SERVICE", "BAPB", "STORE"
    ];

    private static readonly Regex NonAlnumRx = new(@"[^\p{L}\p{Nd}]+", RegexOptions.Compiled);

    public static string CanonicalDisplayName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var s = NormalizeSpaces(raw.Trim());
        s = Regex.Replace(s, @"\s*/\s*", "/ ");
        s = Regex.Replace(s, @"\s{2,}", " ");

        if (s.Length > 80)
        {
            var slash = s.IndexOf('/');
            if (slash > 0 && slash < 40)
                s = s[..slash].Trim();
            else
                s = s[..80].TrimEnd();
        }

        return s;
    }

    public static string NormalizeKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var upper = name.Trim().ToUpperInvariant();
        upper = upper.Replace('/', ' ');
        upper = NonAlnumRx.Replace(upper, " ");
        upper = NormalizeSpaces(upper);

        var tokens = upper.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1 && !NoiseTokens.Contains(t, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (tokens.Count == 0)
            return NonAlnumRx.Replace(name.ToUpperInvariant(), "");

        return string.Join(' ', tokens);
    }

    public static CounterpartyRecord? FindBestMatch(string? rawName, IReadOnlyList<CounterpartyRecord> existing)
    {
        if (string.IsNullOrWhiteSpace(rawName) || existing.Count == 0)
            return null;

        var key = NormalizeKey(rawName);
        if (key.Length < 2)
            return null;

        CounterpartyRecord? best = null;
        var bestScore = 0.0;

        foreach (var cp in existing)
        {
            var score = ScoreMatch(key, NormalizeKey(cp.Name));
            if (score > bestScore)
            {
                bestScore = score;
                best = cp;
            }
        }

        return bestScore >= 0.82 ? best : null;
    }

    private static double ScoreMatch(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return 0;

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return 1.0;

        if (a.Length >= 5 && b.Length >= 5)
        {
            if (a.Contains(b, StringComparison.OrdinalIgnoreCase)
                || b.Contains(a, StringComparison.OrdinalIgnoreCase))
                return 0.9;
        }

        var dist = LevenshteinDistance(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return 1.0 - (double)dist / maxLen;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    private static string NormalizeSpaces(string s) =>
        Regex.Replace(s.Trim(), @"\s+", " ");
}
