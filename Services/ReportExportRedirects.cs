using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace MiniFinance.Services;

internal static class ReportExportRedirects
{
    public static Task RedirectToUnifiedExport(HttpContext ctx, string format)
    {
        var parsed = QueryHelpers.ParseQuery(ctx.Request.QueryString.Value ?? "");
        var pairs = new List<KeyValuePair<string, string?>>();

        foreach (var kv in parsed)
        {
            if (kv.Key.Equals("tab", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var v in kv.Value)
                pairs.Add(new(kv.Key, v));
        }

        pairs.RemoveAll(p => p.Key.Equals("format", StringComparison.OrdinalIgnoreCase));
        pairs.Add(new("format", format));

        if (!pairs.Any(p => p.Key.Equals("report", StringComparison.OrdinalIgnoreCase)))
        {
            var tab = parsed.TryGetValue("tab", out var tabVals) ? tabVals.ToString() : "";
            var report = format.Equals("csv", StringComparison.OrdinalIgnoreCase)
                ? "transactions"
                : tab.Equals("pl", StringComparison.OrdinalIgnoreCase) ? "pl"
                : tab.Equals("cashflow", StringComparison.OrdinalIgnoreCase) ? "cashflow"
                : tab.Equals("categories", StringComparison.OrdinalIgnoreCase) ? "categories"
                : "full";
            pairs.Add(new("report", report));
        }

        ctx.Response.Redirect("/api/reports/export" + QueryString.Create(pairs));
        return Task.CompletedTask;
    }
}
