using MiniFinance.Services;

namespace MiniFinance.Tests;

public class ForecastChartSeriesBuilderTests
{
    [Fact]
    public void Build_starts_from_today_with_current_balance()
    {
        var today = DateTime.Today;
        var series = ForecastChartSeriesBuilder.Build(
            12000m,
            new[] { (today.AddDays(5), 11000m), (today.AddDays(10), 9000m) },
            Array.Empty<(DateTime Start, DateTime? End)>());

        Assert.Equal(today.ToString("yyyy-MM-dd"), series.Dates[0]);
        Assert.Equal(12000d, series.Balances[0]);
        Assert.True(series.Dates.Length >= 3);
    }

    [Fact]
    public void Build_maps_gap_ranges_to_series_indices()
    {
        var today = DateTime.Today;
        var series = ForecastChartSeriesBuilder.Build(
            5000m,
            Enumerable.Range(1, 14).Select(i => (today.AddDays(i), 5000m - i * 200m)),
            new[] { (today.AddDays(5), (DateTime?)today.AddDays(8)) });

        Assert.NotEmpty(series.GapRanges);
        Assert.True(series.GapRanges[0][0] <= series.GapRanges[0][1]);
    }
}
