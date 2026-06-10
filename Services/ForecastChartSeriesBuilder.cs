namespace MiniFinance.Services;

public static class ForecastChartSeriesBuilder
{
    public sealed record Series(string[] Dates, double[] Balances, int[][] GapRanges);

    public static Series Build(
        decimal currentBalance,
        IEnumerable<(DateTime Date, decimal Balance)> forecastPoints,
        IEnumerable<(DateTime Start, DateTime? End)> gaps)
    {
        var today = DateTime.Today;
        var dates = new List<string> { today.ToString("yyyy-MM-dd") };
        var balances = new List<double> { (double)currentBalance };

        foreach (var point in forecastPoints.OrderBy(p => p.Date))
        {
            if (point.Date.Date <= today)
                continue;

            dates.Add(point.Date.ToString("yyyy-MM-dd"));
            balances.Add((double)point.Balance);
        }

        if (dates.Count == 1)
        {
            dates.Add(today.AddDays(1).ToString("yyyy-MM-dd"));
            balances.Add(balances[0]);
        }

        var gapRanges = gaps
            .Select(g =>
            {
                var end = (g.End ?? g.Start).Date;
                var startIdx = FindIndexOnOrAfter(dates, g.Start.Date);
                var endIdx = FindIndexOnOrAfter(dates, end);
                return new[] { startIdx, Math.Max(startIdx, endIdx) };
            })
            .Where(r => r[0] < dates.Count)
            .ToArray();

        return new Series(dates.ToArray(), balances.ToArray(), gapRanges);
    }

    private static int FindIndexOnOrAfter(IReadOnlyList<string> dates, DateTime target)
    {
        for (var i = 0; i < dates.Count; i++)
        {
            if (DateTime.Parse(dates[i]) >= target.Date)
                return i;
        }

        return dates.Count - 1;
    }
}
