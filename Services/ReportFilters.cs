using Microsoft.AspNetCore.Http;

namespace MiniFinance.Services;

public sealed class ReportFilters
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string PeriodType { get; set; } = "Month";
    public int? ProjectId { get; set; }
    public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();
    public string? Department { get; set; }
    public string CompareMode { get; set; } = "mom";
    public int ForecastDays { get; set; } = 90;
    public bool IncludeTaxReserve { get; set; } = true;
    public string ExportReport { get; set; } = "full";

    public static ReportFilters FromQuery(IQueryCollection qs, DateTime? anchorMonth = null)
    {
        DateTime.TryParse(qs["start"], out var start);
        DateTime.TryParse(qs["end"], out var end);
        var periodType = qs["periodType"].ToString();
        if (string.IsNullOrWhiteSpace(periodType)) periodType = "Month";

        int? projectId = int.TryParse(qs["projectId"], out var pid) && pid > 0 ? pid : null;

        var categories = qs["categories"].ToString();
        IReadOnlyList<string> catList = string.IsNullOrWhiteSpace(categories)
            ? Array.Empty<string>()
            : categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var compare = qs["compareMode"].ToString();
        if (string.IsNullOrWhiteSpace(compare)) compare = "mom";

        int.TryParse(qs["forecastDays"], out var forecastDays);
        if (forecastDays is not (30 or 60 or 90)) forecastDays = 90;

        bool.TryParse(qs["includeTaxReserve"], out var includeTaxReserve);

        var report = qs["report"].ToString();
        if (string.IsNullOrWhiteSpace(report)) report = qs["tab"].ToString();
        if (string.IsNullOrWhiteSpace(report)) report = "full";

        if (start == default || end == default)
        {
            var anchor = anchorMonth ?? DateTime.Today;
            (start, end) = ResolvePeriodRange(anchor, periodType);
        }

        if (start > end) (start, end) = (end, start);
        if ((end - start).TotalDays > 1095) start = end.AddYears(-3);

        return new ReportFilters
        {
            Start = start.Date,
            End = end.Date,
            PeriodType = periodType,
            ProjectId = projectId,
            Categories = catList,
            Department = string.IsNullOrWhiteSpace(qs["department"]) ? null : qs["department"].ToString(),
            CompareMode = compare,
            ForecastDays = forecastDays,
            IncludeTaxReserve = qs.ContainsKey("includeTaxReserve") ? includeTaxReserve : true,
            ExportReport = report
        };
    }

    public static (DateTime Start, DateTime End) ResolvePeriodRange(
        DateTime anchor,
        string periodType,
        DateTime? customStart = null,
        DateTime? customEnd = null)
    {
        if (periodType.Equals("Custom", StringComparison.OrdinalIgnoreCase)
            && customStart.HasValue && customEnd.HasValue)
        {
            var s = customStart.Value.Date;
            var e = customEnd.Value.Date;
            if (s > e) (s, e) = (e, s);
            return (s, e);
        }

        if (periodType.Equals("Quarter", StringComparison.OrdinalIgnoreCase))
        {
            var quarter = ((anchor.Month - 1) / 3) + 1;
            var start = new DateTime(anchor.Year, (quarter - 1) * 3 + 1, 1);
            return (start, start.AddMonths(3).AddDays(-1));
        }
        if (periodType.Equals("Year", StringComparison.OrdinalIgnoreCase))
        {
            var start = new DateTime(anchor.Year, 1, 1);
            return (start, new DateTime(anchor.Year, 12, 31));
        }
        var mstart = new DateTime(anchor.Year, anchor.Month, 1);
        return (mstart, mstart.AddMonths(1).AddDays(-1));
    }

    public (DateTime Start, DateTime End) GetComparisonPeriod()
    {
        if (CompareMode.Equals("none", StringComparison.OrdinalIgnoreCase))
            return (Start, End);

        var days = (End - Start).Days + 1;
        return CompareMode.ToLowerInvariant() switch
        {
            "yoy" => (Start.AddYears(-1), End.AddYears(-1)),
            "mom" when PeriodType.Equals("Month", StringComparison.OrdinalIgnoreCase) =>
                (Start.AddMonths(-1), End.AddMonths(-1)),
            "mom" when PeriodType.Equals("Quarter", StringComparison.OrdinalIgnoreCase) =>
                (Start.AddMonths(-3), End.AddMonths(-3)),
            "mom" when PeriodType.Equals("Year", StringComparison.OrdinalIgnoreCase) =>
                (Start.AddYears(-1), End.AddYears(-1)),
            "mom" when PeriodType.Equals("Custom", StringComparison.OrdinalIgnoreCase) =>
                (Start.AddDays(-days), End.AddDays(-days)),
            _ => (Start.AddDays(-days), End.AddDays(-days))
        };
    }

    public string ToQueryString(string? format = null, string? report = null)
    {
        var parts = new List<string>
        {
            $"start={Start:yyyy-MM-dd}",
            $"end={End:yyyy-MM-dd}",
            $"periodType={Uri.EscapeDataString(PeriodType)}",
            $"compareMode={Uri.EscapeDataString(CompareMode)}",
            $"forecastDays={ForecastDays}",
            $"includeTaxReserve={IncludeTaxReserve.ToString().ToLowerInvariant()}"
        };
        if (ProjectId.HasValue) parts.Add($"projectId={ProjectId}");
        if (Categories.Count > 0) parts.Add($"categories={Uri.EscapeDataString(string.Join(",", Categories))}");
        if (!string.IsNullOrWhiteSpace(Department)) parts.Add($"department={Uri.EscapeDataString(Department)}");
        if (!string.IsNullOrWhiteSpace(format)) parts.Add($"format={format}");
        if (!string.IsNullOrWhiteSpace(report)) parts.Add($"report={report}");
        return string.Join("&", parts);
    }

    public string DescribeFilters(string? projectName = null)
    {
        var parts = new List<string> { $"{Start:dd.MM.yyyy} — {End:dd.MM.yyyy}" };
        if (ProjectId.HasValue && !string.IsNullOrEmpty(projectName))
            parts.Add($"проект: {projectName}");
        if (Categories.Count > 0)
            parts.Add($"категории: {string.Join(", ", Categories)}");
        return string.Join(" · ", parts);
    }
}
