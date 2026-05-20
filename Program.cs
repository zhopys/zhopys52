using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;
using MiniFinance.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authentication;
using MiniFinance.Components.Account;

var seedDiplomaDemo = args.Any(a => a.Equals("--seed-diploma-demo", StringComparison.OrdinalIgnoreCase));
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

var requireConfirmedEmail = builder.Configuration.GetValue("Identity:RequireConfirmedEmail", false);

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = requireConfirmedEmail;
        options.User.RequireUniqueEmail = true;
        options.Password.RequireDigit = false;
        options.Password.RequiredLength = 3;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OwnerOnly", p => p.RequireRole(AppRoles.Owner));
    options.AddPolicy("CanViewReports", p => p.RequireRole(AppRoles.Owner, AppRoles.Accountant, AppRoles.Manager));
    options.AddPolicy("CanManageSettings", p => p.RequireRole(AppRoles.Owner));
});

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication()
        .AddGoogle(o =>
        {
            o.ClientId = googleClientId;
            o.ClientSecret = googleClientSecret;
        });
}

// Core services
builder.Services.AddScoped<ICsvParser, CsvParser>();
builder.Services.AddScoped<IBankPdfStatementParser, BankPdfStatementParser>();
builder.Services.AddScoped<ICategorizationService, CategorizationService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IForecastingService, ForecastingService>();
builder.Services.AddScoped<IReportAnalyticsService, ReportAnalyticsService>();
builder.Services.AddScoped<IExtendedReportService, ExtendedReportService>();
builder.Services.AddScoped<IReportsDashboardService, ReportsDashboardService>();
builder.Services.AddScoped<IReportExportService, ReportExportService>();
builder.Services.AddScoped<ITransactionDataStatusService, TransactionDataStatusService>();
builder.Services.AddScoped<IAccountingIntegrationService, AccountingIntegrationService>();
builder.Services.AddScoped<IReportPdfService, ReportPdfService>();
builder.Services.AddScoped<ITaxService, TaxService>();
builder.Services.AddScoped<ITaxAutoRuleService, TaxAutoRuleService>();
builder.Services.AddScoped<ITagService, TagService>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services.AddScoped<ICounterpartyService, CounterpartyService>();
builder.Services.AddScoped<IDebtService, DebtService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<IBalanceReportService, BalanceReportService>();
builder.Services.AddScoped<IPaymentCalendarService, PaymentCalendarService>();
builder.Services.AddScoped<IDataScopeService, DataScopeService>();
builder.Services.AddScoped<IUserContextService, UserContextService>();
builder.Services.AddScoped<ITeamService, TeamService>(); // UI отключено; сервис оставлен для будущего включения
builder.Services.AddScoped<MiniFinance.Components.Account.IdentityRedirectManager>();
builder.Services.AddScoped<Microsoft.AspNetCore.Identity.IEmailSender<ApplicationUser>, IdentitySmtpEmailSender>();

// Email notification services
builder.Services.Configure<MiniFinance.Services.SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));
builder.Services.Configure<MiniFinance.Services.NotificationSettings>(builder.Configuration.GetSection("NotificationSettings"));
builder.Services.AddScoped<MiniFinance.Services.INotificationEmailService, MiniFinance.Services.NotificationEmailService>();
builder.Services.AddHostedService<MiniFinance.Services.NotificationBackgroundService>();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

// CSV export endpoint
app.MapGet("/reports/export/csv", async (HttpContext http, ApplicationDbContext db, UserManager<ApplicationUser> userManager, IReportService reportService) =>
{
    var user = await userManager.GetUserAsync(http.User);
    if (user == null) return Results.Unauthorized();

    var qs = http.Request.Query;
    DateTime.TryParse(qs["start"], out var start);
    DateTime.TryParse(qs["end"], out var end);
    var tab = qs["tab"].ToString();
    int.TryParse(qs["projectId"], out var projectId);

    if (start == default) start = DateTime.Today.AddMonths(-1);
    if (end == default) end = DateTime.Today;

    var transactions = await db.Transactions
        .Include(t => t.Project)
        .Where(t => t.UserId == user.Id && t.Date >= start && t.Date <= end)
        .OrderBy(t => t.Date)
        .ToListAsync();

    var sb = new System.Text.StringBuilder();

    if (!string.IsNullOrEmpty(tab) && tab.Equals("projects", StringComparison.OrdinalIgnoreCase))
    {
        var tx = transactions;
        if (projectId > 0) tx = tx.Where(t => t.ProjectId == projectId).ToList();
        var projReport = reportService.GetProjectReport(tx);
        sb.AppendLine("Project,Income,Expense,Profit,Transactions");
        foreach (var p in projReport)
        {
            sb.AppendLine($"{Escape(p.Project)},{p.Income.ToString(System.Globalization.CultureInfo.InvariantCulture)},{p.Expense.ToString(System.Globalization.CultureInfo.InvariantCulture)},{p.Profit.ToString(System.Globalization.CultureInfo.InvariantCulture)},{p.Transactions}");
        }
    }
    else if (!string.IsNullOrEmpty(tab) && tab.Equals("categories", StringComparison.OrdinalIgnoreCase))
    {
        var catReport = reportService.GetCategoryBreakdown(transactions);
        sb.AppendLine("Category,Amount,Count,Percentage,Type");
        foreach (var c in catReport.ExpenseByCategory)
        {
            sb.AppendLine($"{Escape(c.Category)},{c.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)},{c.Count},{c.Percentage.ToString(System.Globalization.CultureInfo.InvariantCulture)},Expense");
        }
        foreach (var c in catReport.IncomeByCategory)
        {
            sb.AppendLine($"{Escape(c.Category)},{c.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)},{c.Count},{c.Percentage.ToString(System.Globalization.CultureInfo.InvariantCulture)},Income");
        }
    }
    else
    {
        sb.AppendLine("Date,Description,Category,Amount,Project");
        foreach (var t in transactions)
        {
            string esc(string? s) => '"' + (s ?? string.Empty).Replace("\"", "\"\"") + '"';
            sb.AppendLine($"{t.Date:yyyy-MM-dd},{esc(t.Description)},{esc(t.Category)},{t.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)},{esc(t.Project?.Name)}");
        }
    }

    // Prepend UTF-8 BOM so Excel on Windows recognizes UTF-8 with Russian characters correctly
    var bom = new byte[] { 0xEF, 0xBB, 0xBF };
    var content = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    var bytes = new byte[bom.Length + content.Length];
    Buffer.BlockCopy(bom, 0, bytes, 0, bom.Length);
    Buffer.BlockCopy(content, 0, bytes, bom.Length, content.Length);
    var fileName = $"report_{tab ?? "all"}_{start:yyyyMMdd}_{end:yyyyMMdd}.csv";
    return Results.File(bytes, "text/csv; charset=utf-8", fileName);

    static string Escape(string? s) => '"' + (s ?? string.Empty).Replace("\"", "\"\"") + '"';
}).RequireAuthorization();

// Excel export endpoint
app.MapGet("/reports/export/xlsx", async (HttpContext http, ApplicationDbContext db, UserManager<ApplicationUser> userManager, IReportService reportService) =>
{
    var user = await userManager.GetUserAsync(http.User);
    if (user == null) return Results.Unauthorized();

    var qs = http.Request.Query;
    DateTime.TryParse(qs["start"], out var start);
    DateTime.TryParse(qs["end"], out var end);
    var tab = qs["tab"].ToString();
    int.TryParse(qs["projectId"], out var projectId);

    if (start == default) start = DateTime.Today.AddMonths(-1);
    if (end == default) end = DateTime.Today;

    var transactions = await db.Transactions
        .Include(t => t.Project)
        .Where(t => t.UserId == user.Id && t.Date >= start && t.Date <= end)
        .OrderBy(t => t.Date)
        .ToListAsync();

    // Use streaming to handle large datasets: ClosedXML supports saving from memory, but we will build worksheets carefully
    using var wb = new XLWorkbook();

    if (!string.IsNullOrEmpty(tab) && tab.Equals("projects", StringComparison.OrdinalIgnoreCase))
    {
        var tx = transactions;
        if (projectId > 0) tx = tx.Where(t => t.ProjectId == projectId).ToList();
        var projects = reportService.GetProjectReport(tx);
        var wsProj = wb.Worksheets.Add("Projects");
        wsProj.Cell(1, 1).Value = "Project";
        wsProj.Cell(1, 2).Value = "Income";
        wsProj.Cell(1, 3).Value = "Expense";
        wsProj.Cell(1, 4).Value = "Profit";
        wsProj.Cell(1, 5).Value = "Transactions";
        for (int i = 0; i < projects.Count; i++)
        {
            var r = i + 2;
            var p = projects[i];
            wsProj.Cell(r, 1).Value = p.Project;
            wsProj.Cell(r, 2).Value = p.Income;
            wsProj.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            wsProj.Cell(r, 3).Value = p.Expense;
            wsProj.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
            wsProj.Cell(r, 4).Value = p.Profit;
            wsProj.Cell(r, 4).Style.NumberFormat.Format = "#,##0.00";
            wsProj.Cell(r, 5).Value = p.Transactions;
        }
        // Format headers and table
        var lastRowProj = projects.Count + 1;
        var lastColProj = 5;
        var projRange = wsProj.Range(1, 1, lastRowProj, lastColProj);
        // Create a styled table for better Excel rendering
        var projTableName = "ProjectsTable_" + System.Guid.NewGuid().ToString("N");
        var projTable = projRange.CreateTable(projTableName);
        projTable.Theme = XLTableTheme.TableStyleMedium2;
        // Conditional formatting: highlight negative balances in numeric columns (B..D)
        for (int c = 2; c <= 4; c++)
        {
            var colRange = wsProj.Range(2, c, lastRowProj, c);
            var cf = colRange.AddConditionalFormat();
            cf.WhenLessThan(0).Fill.SetBackgroundColor(XLColor.FromHtml("#fdecea"));
            cf.WhenLessThan(0).Font.SetFontColor(XLColor.DarkRed);
        }
        wsProj.Row(1).Style.Font.Bold = true;
        wsProj.SheetView.FreezeRows(1);
        // Number formatting and alignment
        wsProj.Column(2).Style.NumberFormat.Format = "#,##0.00 \"Br\"";
        wsProj.Column(3).Style.NumberFormat.Format = "#,##0.00 \"Br\"";
        wsProj.Column(4).Style.NumberFormat.Format = "#,##0.00 \"Br\"";
        wsProj.Columns().AdjustToContents();
    }
    else if (!string.IsNullOrEmpty(tab) && tab.Equals("pl", StringComparison.OrdinalIgnoreCase))
    {
        var pl = reportService.GetProfitLossReport(transactions, start, end);
        var ws = wb.Worksheets.Add("P&L");
        ws.Cell(1, 1).Value = "Показатель";
        ws.Cell(1, 2).Value = "Сумма";
        ws.Cell(2, 1).Value = "Доходы";
        ws.Cell(2, 2).Value = pl.TotalIncome;
        ws.Cell(3, 1).Value = "Расходы";
        ws.Cell(3, 2).Value = pl.TotalExpense;
        ws.Cell(4, 1).Value = "Чистая прибыль";
        ws.Cell(4, 2).Value = pl.NetProfit;
        ws.Cell(5, 1).Value = "Маржинальность %";
        ws.Cell(5, 2).Value = (double)pl.ProfitMargin;
        int row = 7;
        ws.Cell(row, 1).Value = "Расходы по категориям";
        row++;
        foreach (var c in pl.ExpenseByCategory)
        {
            ws.Cell(row, 1).Value = c.Category;
            ws.Cell(row, 2).Value = c.Amount;
            ws.Cell(row, 3).Value = (double)c.Percentage;
            row++;
        }
        ws.Columns().AdjustToContents();
    }
    else if (!string.IsNullOrEmpty(tab) && tab.Equals("cashflow", StringComparison.OrdinalIgnoreCase))
    {
        var cf = reportService.GetCashFlowStatement(transactions, start, end);
        var ws = wb.Worksheets.Add("Cash Flow");
        ws.Cell(1, 1).Value = "Деятельность";
        ws.Cell(1, 2).Value = "Сумма";
        ws.Cell(2, 1).Value = "Операционная";
        ws.Cell(2, 2).Value = cf.OperatingCashFlow;
        ws.Cell(3, 1).Value = "Инвестиционная";
        ws.Cell(3, 2).Value = cf.InvestmentCashFlow;
        ws.Cell(4, 1).Value = "Финансовая";
        ws.Cell(4, 2).Value = cf.FinancingCashFlow;
        ws.Cell(5, 1).Value = "Чистый поток";
        ws.Cell(5, 2).Value = cf.NetCashFlow;
        int row = 7;
        foreach (var c in cf.CategoryDetails)
        {
            ws.Cell(row, 1).Value = c.Category;
            ws.Cell(row, 2).Value = c.Income;
            ws.Cell(row, 3).Value = c.Expense;
            ws.Cell(row, 4).Value = c.NetFlow;
            row++;
        }
        ws.Columns().AdjustToContents();
    }
    else if (!string.IsNullOrEmpty(tab) && tab.Equals("categories", StringComparison.OrdinalIgnoreCase))
    {
        var catReport = reportService.GetCategoryBreakdown(transactions);
        var wsCat = wb.Worksheets.Add("Categories");
        wsCat.Cell(1, 1).Value = "Category";
        wsCat.Cell(1, 2).Value = "Amount";
        wsCat.Cell(1, 3).Value = "Count";
        wsCat.Cell(1, 4).Value = "Percentage";
        wsCat.Cell(1, 5).Value = "Type";
        for (int i = 0; i < catReport.ExpenseByCategory.Count; i++)
        {
            var r = i + 2;
            var c = catReport.ExpenseByCategory[i];
            wsCat.Cell(r, 1).Value = c.Category;
            wsCat.Cell(r, 2).Value = c.Amount;
            wsCat.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            wsCat.Cell(r, 3).Value = c.Count;
            wsCat.Cell(r, 4).Value = (double)c.Percentage;
            wsCat.Cell(r, 4).Style.NumberFormat.Format = "0.0%";
            wsCat.Cell(r, 5).Value = "Expense";
        }
        for (int i = 0; i < catReport.IncomeByCategory.Count; i++)
        {
            var r = i + 2 + catReport.ExpenseByCategory.Count;
            var c = catReport.IncomeByCategory[i];
            wsCat.Cell(r, 1).Value = c.Category;
            wsCat.Cell(r, 2).Value = c.Amount;
            wsCat.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            wsCat.Cell(r, 3).Value = c.Count;
            wsCat.Cell(r, 4).Value = (double)c.Percentage;
            wsCat.Cell(r, 4).Style.NumberFormat.Format = "0.0%";
            wsCat.Cell(r, 5).Value = "Income";
        }
        var lastRowCat = catReport.ExpenseByCategory.Count + catReport.IncomeByCategory.Count + 1;
        var lastColCat = 5;
        var catRange = wsCat.Range(1, 1, lastRowCat, lastColCat);
        var catTableName = "CategoriesTable_" + System.Guid.NewGuid().ToString("N");
        var catTable = catRange.CreateTable(catTableName);
        catTable.Theme = XLTableTheme.TableStyleMedium9;
        // Conditional formatting for categories amounts (column B)
        var catAmountRange = wsCat.Range(2, 2, lastRowCat, 2);
        var catCf = catAmountRange.AddConditionalFormat();
        catCf.WhenLessThan(0).Fill.SetBackgroundColor(XLColor.FromHtml("#fdecea"));
        catCf.WhenLessThan(0).Font.SetFontColor(XLColor.DarkRed);
        wsCat.Row(1).Style.Font.Bold = true;
        wsCat.SheetView.FreezeRows(1);
        wsCat.Column(2).Style.NumberFormat.Format = "#,##0.00 \"Br\"";
        wsCat.Column(4).Style.NumberFormat.Format = "0.0%";
        wsCat.Columns().AdjustToContents();
    }
    else
    {
        var ws = wb.Worksheets.Add("Transactions");
        ws.Cell(1, 1).Value = "Date";
        ws.Cell(1, 2).Value = "Description";
        ws.Cell(1, 3).Value = "Category";
        ws.Cell(1, 4).Value = "Amount";
        for (int i = 0; i < transactions.Count; i++)
        {
            var r = i + 2;
            var t = transactions[i];
            ws.Cell(r, 1).Value = t.Date;
            ws.Cell(r, 1).Style.DateFormat.Format = "dd.MM.yyyy";
            ws.Cell(r, 2).Value = t.Description;
            ws.Cell(r, 3).Value = t.Category;
            ws.Cell(r, 4).Value = t.Amount;
            ws.Cell(r, 4).Style.NumberFormat.Format = "#,##0.00";
        }
        var lastRow = transactions.Count + 1;
        var lastCol = 4;
        var txRange = ws.Range(1, 1, lastRow, lastCol);
        var txTableName = "TransactionsTable_" + System.Guid.NewGuid().ToString("N");
        var txTable = txRange.CreateTable(txTableName);
        txTable.Theme = XLTableTheme.TableStyleMedium9;
        // Conditional formatting: transactions amount (column 4)
        var txAmountRange = ws.Range(2, 4, lastRow, 4);
        var txCf = txAmountRange.AddConditionalFormat();
        txCf.WhenLessThan(0).Fill.SetBackgroundColor(XLColor.FromHtml("#fdecea"));
        txCf.WhenLessThan(0).Font.SetFontColor(XLColor.DarkRed);
        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);
        ws.Column(1).Style.DateFormat.Format = "dd.MM.yyyy";
        ws.Column(4).Style.NumberFormat.Format = "#,##0.00 \"Br\"";
        ws.Column(4).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Right;
        ws.Columns().AdjustToContents();

        var cashflow = reportService.GetCashflow(transactions);
        var wsCash = wb.Worksheets.Add("Cashflow");
        wsCash.Cell(1, 1).Value = "Date";
        wsCash.Cell(1, 2).Value = "Income";
        wsCash.Cell(1, 3).Value = "Expense";
        wsCash.Cell(1, 4).Value = "Cumulative Balance";
        for (int i = 0; i < cashflow.Count; i++)
        {
            var r = i + 2;
            var c = cashflow[i];
            wsCash.Cell(r, 1).Value = c.Date;
            wsCash.Cell(r, 1).Style.DateFormat.Format = "dd.MM.yyyy";
            wsCash.Cell(r, 2).Value = c.Income;
            wsCash.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            wsCash.Cell(r, 3).Value = c.Expense;
            wsCash.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
            wsCash.Cell(r, 4).Value = c.Balance;
            wsCash.Cell(r, 4).Style.NumberFormat.Format = "#,##0.00";
        }
        var lastRowCash = cashflow.Count + 1;
        var lastColCash = 4;
        var cashRange = wsCash.Range(1, 1, lastRowCash, lastColCash);
        var cashTableName = "CashflowTable_" + System.Guid.NewGuid().ToString("N");
        var cashTable = cashRange.CreateTable(cashTableName);
        cashTable.Theme = XLTableTheme.TableStyleMedium2;
        // Conditional formatting for cashflow columns (B..D)
        for (int c = 2; c <= 4; c++)
        {
            var cashColRange = wsCash.Range(2, c, lastRowCash, c);
            var cf = cashColRange.AddConditionalFormat();
            cf.WhenLessThan(0).Fill.SetBackgroundColor(XLColor.FromHtml("#fdecea"));
            cf.WhenLessThan(0).Font.SetFontColor(XLColor.DarkRed);
        }
        wsCash.Row(1).Style.Font.Bold = true;
        wsCash.SheetView.FreezeRows(1);
        wsCash.Column(1).Style.DateFormat.Format = "dd.MM.yyyy";
        wsCash.Column(2).Style.NumberFormat.Format = "#,##0.00 \"Br\"";
        wsCash.Column(3).Style.NumberFormat.Format = "#,##0.00 \"Br\"";
        wsCash.Column(4).Style.NumberFormat.Format = "#,##0.00 \"Br\"";
        wsCash.Columns().AdjustToContents();
    }

    using var ms = new System.IO.MemoryStream();
    wb.SaveAs(ms);
    ms.Position = 0;
    var fileName = $"report_{tab ?? "all"}_{start:yyyyMMdd}_{end:yyyyMMdd}.xlsx";
    return Results.File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
}).RequireAuthorization();

// Legacy HTML Excel export
app.MapGet("/reports/export/excel", async (HttpContext http, ApplicationDbContext db, UserManager<ApplicationUser> userManager) =>
{
    var user = await userManager.GetUserAsync(http.User);
    if (user == null) return Results.Unauthorized();

    var qs = http.Request.Query;
    DateTime.TryParse(qs["start"], out var start);
    DateTime.TryParse(qs["end"], out var end);

    if (start == default) start = DateTime.Today.AddMonths(-1);
    if (end == default) end = DateTime.Today;

    var transactions = await db.Transactions
        .Where(t => t.UserId == user.Id && t.Date >= start && t.Date <= end)
        .OrderBy(t => t.Date)
        .ToListAsync();

    var sb = new System.Text.StringBuilder();
    sb.AppendLine("<table border=1>");
    sb.AppendLine("<tr><th>Date</th><th>Description</th><th>Category</th><th>Amount</th></tr>");
    foreach (var t in transactions)
    {
        sb.AppendLine($"<tr><td>{t.Date:dd.MM.yyyy}</td><td>{System.Net.WebUtility.HtmlEncode(t.Description)}</td><td>{System.Net.WebUtility.HtmlEncode(t.Category)}</td><td>{t.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}</td></tr>");
    }
    sb.AppendLine("</table>");

    var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    var fileName = $"transactions_{start:yyyyMMdd}_{end:yyyyMMdd}.xls";
    return Results.File(bytes, "application/vnd.ms-excel", fileName);
}).RequireAuthorization();

// PDF export
app.MapGet("/reports/export/pdf", async (
    HttpContext http,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IReportService reportService,
    IReportPdfService pdfService) =>
{
    var user = await userManager.GetUserAsync(http.User);
    if (user == null) return Results.Unauthorized();

    var qs = http.Request.Query;
    DateTime.TryParse(qs["start"], out var start);
    DateTime.TryParse(qs["end"], out var end);
    var tab = qs["tab"].ToString();

    if (start == default) start = DateTime.Today.AddMonths(-1);
    if (end == default) end = DateTime.Today;

    var transactions = await db.Transactions
        .Where(t => t.UserId == user.Id && t.Date >= start && t.Date <= end)
        .ToListAsync();

    byte[] bytes;
    var fileStem = tab.Equals("cashflow", StringComparison.OrdinalIgnoreCase) ? "cashflow" : "pl";

    if (tab.Equals("cashflow", StringComparison.OrdinalIgnoreCase))
    {
        var cf = reportService.GetCashFlowStatement(transactions, start, end);
        bytes = pdfService.GenerateCashFlowPdf(cf, start, end);
    }
    else
    {
        var pl = reportService.GetProfitLossReport(transactions, start, end);
        bytes = pdfService.GenerateProfitLossPdf(pl, start, end);
    }

    return Results.File(bytes, "application/pdf", $"report_{fileStem}_{start:yyyyMMdd}_{end:yyyyMMdd}.pdf");
}).RequireAuthorization();

// Reports API
var reportsApi = app.MapGroup("/api/reports").RequireAuthorization();

reportsApi.MapGet("/analytics", async (
    HttpContext ctx,
    IReportAnalyticsService analytics,
    UserManager<ApplicationUser> um) =>
{
    var user = await um.GetUserAsync(ctx.User);
    if (user == null) return Results.Unauthorized();
    var filters = ReportFilters.FromQuery(ctx.Request.Query);
    return Results.Ok(await analytics.BuildSnapshotAsync(user.Id, filters));
});

reportsApi.MapGet("/dashboard", async (
    HttpContext ctx,
    IReportAnalyticsService analytics,
    UserManager<ApplicationUser> um) =>
{
    var user = await um.GetUserAsync(ctx.User);
    if (user == null) return Results.Unauthorized();
    var filters = ReportFilters.FromQuery(ctx.Request.Query);
    var snapshot = await analytics.BuildSnapshotAsync(user.Id, filters);
    return Results.Ok(snapshot);
});

reportsApi.MapGet("/export", async (
    HttpContext ctx,
    IReportAnalyticsService analytics,
    IReportExportService export,
    UserManager<ApplicationUser> um) =>
{
    var user = await um.GetUserAsync(ctx.User);
    if (user == null) return Results.Unauthorized();
    var filters = ReportFilters.FromQuery(ctx.Request.Query);
    var format = ctx.Request.Query["format"].ToString();
    if (string.IsNullOrWhiteSpace(format)) format = "xlsx";
    var snapshot = await analytics.BuildSnapshotAsync(user.Id, filters);
    var (contentType, fileName, data) = export.Export(snapshot, format, user.Id);
    return Results.File(data, contentType, fileName);
});

reportsApi.MapPost("/export", async (
    HttpContext ctx,
    IReportAnalyticsService analytics,
    IReportExportService export,
    UserManager<ApplicationUser> um,
    ReportFilters? body) =>
{
    var user = await um.GetUserAsync(ctx.User);
    if (user == null) return Results.Unauthorized();
    var filters = body ?? ReportFilters.FromQuery(ctx.Request.Query);
    var format = ctx.Request.Query["format"].ToString();
    if (string.IsNullOrWhiteSpace(format)) format = "xlsx";
    var snapshot = await analytics.BuildSnapshotAsync(user.Id, filters);
    var (contentType, fileName, data) = export.Export(snapshot, format, user.Id);
    return Results.File(data, contentType, fileName);
});

reportsApi.MapGet("/forecast", async (
    HttpContext ctx,
    IReportsDashboardService dashboard,
    UserManager<ApplicationUser> um,
    int days = 90) =>
{
    var user = await um.GetUserAsync(ctx.User);
    if (user == null) return Results.Unauthorized();
    var end = DateTime.Today;
    var start = end.AddMonths(-12);
    var dto = await dashboard.GetDashboardAsync(user.Id, start, end, days);
    return Results.Ok(dto.Forecast);
});

var integrationsApi = app.MapGroup("/api/integrations").RequireAuthorization();

integrationsApi.MapGet("/data-status", async (
    HttpContext ctx,
    ITransactionDataStatusService dataStatus,
    UserManager<ApplicationUser> um,
    DateTime? start,
    DateTime? end) =>
{
    var user = await um.GetUserAsync(ctx.User);
    if (user == null) return Results.Unauthorized();
    return Results.Ok(await dataStatus.GetStatusAsync(user.Id, start, end));
});

integrationsApi.MapGet("/accounting/status", async (HttpContext ctx, IAccountingIntegrationService acc, UserManager<ApplicationUser> um) =>
{
    var user = await um.GetUserAsync(ctx.User);
    if (user == null) return Results.Unauthorized();
    return Results.Ok(await acc.GetStatusAsync(user.Id));
});

integrationsApi.MapPost("/accounting/export", async (
    HttpContext ctx,
    IAccountingIntegrationService acc,
    UserManager<ApplicationUser> um,
    DateTime? start,
    DateTime? end) =>
{
    var user = await um.GetUserAsync(ctx.User);
    if (user == null) return Results.Unauthorized();
    var range = IReportsDashboardService.ResolvePeriod(start, end);
    return Results.Ok(await acc.ForceExportAsync(user.Id, range.Start, range.End));
});

app.MapPatch("/api/transactions/{id:int}/category", async (
    int id,
    CategoryPatchRequest body,
    ITransactionService txService,
    UserManager<ApplicationUser> um,
    HttpContext ctx) =>
{
    var user = await um.GetUserAsync(ctx.User);
    if (user == null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(body.Category))
        return Results.BadRequest(new { error = "Category is required" });
    try
    {
        var updated = await txService.UpdateCategoryAsync(id, body.Category, user.Id);
        return Results.Ok(new { updated.Id, updated.Category });
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
}).RequireAuthorization();

app.MapGet("/do-logout", async (HttpContext ctx, SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/login");
});

ConfirmEmailEndpoint.MapConfirmEmailEndpoint(app);
LoginEndpoint.MapLoginEndpoint(app);

app.MapRazorComponents<MiniFinance.Components.App>()
    .AddInteractiveServerRenderMode()
    .DisableAntiforgery();

app.MapAdditionalIdentityEndpoints();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.EnsureCreated();

    var categorization = scope.ServiceProvider.GetRequiredService<ICategorizationService>();
    categorization.EnsureDefaultCategoriesAsync().GetAwaiter().GetResult();

    try
    {
        var connection = dbContext.Database.GetDbConnection();
        connection.Open();

        using (var cmd = connection.CreateCommand())
        {
            // Ensure Projects table exists (INTEGER Status — matches EF enum)
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Projects (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Description TEXT,
                IsDefault INTEGER NOT NULL DEFAULT 0,
                Status INTEGER NOT NULL DEFAULT 1,
                Budget REAL,
                TargetROI REAL,
                StartDate TEXT,
                EndDate TEXT,
                Priority INTEGER,
                ProjectManager TEXT,
                Department TEXT,
                KPI TEXT,
                Risks TEXT,
                Notes TEXT,
                UserId TEXT,
                ROI REAL
            );";
            cmd.ExecuteNonQuery();

            // Drop legacy global unique index on Name (blocks per-user duplicate names)
            cmd.CommandText = "DROP INDEX IF EXISTS IX_Projects_Name;";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "DROP INDEX IF EXISTS \"IX_Projects_Name\";";
            cmd.ExecuteNonQuery();

            // Add missing columns to Projects (EF + raw SQL schemas)
            cmd.CommandText = "PRAGMA table_info('Projects');";
            using var projReader = cmd.ExecuteReader();
            var projColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var projColumnTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (projReader.Read())
            {
                var colName = projReader[1]?.ToString() ?? "";
                projColumns.Add(colName);
                projColumnTypes[colName] = projReader[2]?.ToString() ?? "";
            }
            projReader.Close();

            if (!projColumns.Contains("Department"))
            {
                cmd.CommandText = "ALTER TABLE Projects ADD COLUMN Department TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!projColumns.Contains("TargetROI"))
            {
                cmd.CommandText = "ALTER TABLE Projects ADD COLUMN TargetROI REAL;";
                cmd.ExecuteNonQuery();
            }
            if (!projColumns.Contains("UserId"))
            {
                cmd.CommandText = "ALTER TABLE Projects ADD COLUMN UserId TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!projColumns.Contains("ROI"))
            {
                cmd.CommandText = "ALTER TABLE Projects ADD COLUMN ROI REAL;";
                cmd.ExecuteNonQuery();
            }
            if (!projColumns.Contains("IsDefault"))
            {
                cmd.CommandText = "ALTER TABLE Projects ADD COLUMN IsDefault INTEGER NOT NULL DEFAULT 0;";
                cmd.ExecuteNonQuery();
            }

            // Migrate legacy ROI -> TargetROI
            if (projColumns.Contains("ROI"))
            {
                cmd.CommandText = "UPDATE Projects SET TargetROI = ROI WHERE TargetROI IS NULL AND ROI IS NOT NULL;";
                cmd.ExecuteNonQuery();
            }

            // Normalize text Status values (legacy raw SQL used TEXT 'Active', etc.)
            if (projColumnTypes.TryGetValue("Status", out var statusColType) &&
                statusColType.Equals("TEXT", StringComparison.OrdinalIgnoreCase))
            {
                cmd.CommandText = @"
                    UPDATE Projects SET Status = '0' WHERE Status IN ('Active', '0');
                    UPDATE Projects SET Status = '1' WHERE Status IN ('Planning', '1');
                    UPDATE Projects SET Status = '2' WHERE Status IN ('OnHold', 'On Hold', '2');
                    UPDATE Projects SET Status = '3' WHERE Status IN ('Completed', '3');
                    UPDATE Projects SET Status = '4' WHERE Status IN ('Cancelled', 'Canceled', '4');";
                cmd.ExecuteNonQuery();
            }

            // Normalize decimal strings stored as TEXT (e.g. '2,0' -> 2.0)
            foreach (var col in new[] { "Budget", "TargetROI" })
            {
                if (projColumnTypes.TryGetValue(col, out var colType) &&
                    colType.Equals("TEXT", StringComparison.OrdinalIgnoreCase))
                {
                    cmd.CommandText = $"UPDATE Projects SET {col} = REPLACE({col}, ',', '.') WHERE {col} LIKE '%,%';";
                    cmd.ExecuteNonQuery();
                }
            }

            // Per-user unique name (default projects excluded)
            cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_Projects_UserId_Name ON Projects(UserId, Name) WHERE IsDefault = 0;";
            cmd.ExecuteNonQuery();

            // Ensure indexes
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Projects_UserId ON Projects(UserId);";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Projects_Status ON Projects(Status);";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Projects_Priority ON Projects(Priority);";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Projects_ProjectManager ON Projects(ProjectManager);";
            cmd.ExecuteNonQuery();

            // Ensure Reminders table exists
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS Reminders (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Amount REAL NOT NULL, Category TEXT, Frequency INTEGER NOT NULL DEFAULT 0, Date TEXT NOT NULL, IsPaid INTEGER NOT NULL DEFAULT 0, PaidDate TEXT, UserId TEXT NOT NULL, ProjectId INTEGER);";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Reminders_UserId ON Reminders(UserId);";
            cmd.ExecuteNonQuery();

            // Ensure Categories table exists
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS Categories (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Description TEXT, IsDefault INTEGER NOT NULL DEFAULT 0, Type INTEGER NOT NULL DEFAULT 0, Icon TEXT, Color TEXT);";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_Categories_Name ON Categories(Name);";
            cmd.ExecuteNonQuery();

            // Ensure BankStatements table exists
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS BankStatements (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                FileName TEXT NOT NULL,
                FilePath TEXT NOT NULL,
                UploadDate TEXT NOT NULL,
                BankFormat TEXT NOT NULL,
                TransactionCount INTEGER NOT NULL DEFAULT 0,
                StatementStartDate TEXT,
                StatementEndDate TEXT,
                Status TEXT NOT NULL DEFAULT 'Processed',
                ErrorMessage TEXT
            );";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_BankStatements_UserId_UploadDate ON BankStatements(UserId, UploadDate);";
            cmd.ExecuteNonQuery();

            // Add Keywords to Categories if missing
            cmd.CommandText = "PRAGMA table_info('Categories');";
            using var catReader = cmd.ExecuteReader();
            var catColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (catReader.Read())
            {
                catColumns.Add(catReader[1]?.ToString() ?? "");
            }
            catReader.Close();

            if (!catColumns.Contains("Keywords"))
            {
                cmd.CommandText = "ALTER TABLE Categories ADD COLUMN Keywords TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!catColumns.Contains("Icon"))
            {
                cmd.CommandText = "ALTER TABLE Categories ADD COLUMN Icon TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!catColumns.Contains("Color"))
            {
                cmd.CommandText = "ALTER TABLE Categories ADD COLUMN Color TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!catColumns.Contains("ParentCategoryId"))
            {
                cmd.CommandText = "ALTER TABLE Categories ADD COLUMN ParentCategoryId INTEGER;";
                cmd.ExecuteNonQuery();
            }
            if (!catColumns.Contains("MonthlyBudget"))
            {
                cmd.CommandText = "ALTER TABLE Categories ADD COLUMN MonthlyBudget REAL;";
                cmd.ExecuteNonQuery();
            }
            if (!catColumns.Contains("GroupName"))
            {
                cmd.CommandText = "ALTER TABLE Categories ADD COLUMN GroupName TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!catColumns.Contains("IsHidden"))
            {
                cmd.CommandText = "ALTER TABLE Categories ADD COLUMN IsHidden INTEGER NOT NULL DEFAULT 0;";
                cmd.ExecuteNonQuery();
            }

            // Add missing columns to OrganizationSettings if missing
            cmd.CommandText = "PRAGMA table_info('OrganizationSettings');";
            using var orgReader = cmd.ExecuteReader();
            var orgColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (orgReader.Read())
            {
                orgColumns.Add(orgReader[1]?.ToString() ?? "");
            }
            orgReader.Close();

            if (!orgColumns.Contains("CompanyName"))
            {
                cmd.CommandText = "ALTER TABLE OrganizationSettings ADD COLUMN CompanyName TEXT DEFAULT '';";
                cmd.ExecuteNonQuery();
            }
            if (!orgColumns.Contains("UNP"))
            {
                cmd.CommandText = "ALTER TABLE OrganizationSettings ADD COLUMN UNP TEXT DEFAULT '';";
                cmd.ExecuteNonQuery();
            }
            if (!orgColumns.Contains("ApiKey"))
            {
                cmd.CommandText = "ALTER TABLE OrganizationSettings ADD COLUMN ApiKey TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!orgColumns.Contains("IntegrationUrl"))
            {
                cmd.CommandText = "ALTER TABLE OrganizationSettings ADD COLUMN IntegrationUrl TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!orgColumns.Contains("TaxSystem"))
            {
                cmd.CommandText = "ALTER TABLE OrganizationSettings ADD COLUMN TaxSystem INTEGER NOT NULL DEFAULT 0;";
                cmd.ExecuteNonQuery();
            }
            if (!orgColumns.Contains("MinCashBalance"))
            {
                cmd.CommandText = "ALTER TABLE OrganizationSettings ADD COLUMN MinCashBalance REAL NOT NULL DEFAULT 1000;";
                cmd.ExecuteNonQuery();
            }
            if (!orgColumns.Contains("WeekStartsOn"))
            {
                cmd.CommandText = "ALTER TABLE OrganizationSettings ADD COLUMN WeekStartsOn INTEGER NOT NULL DEFAULT 1;";
                cmd.ExecuteNonQuery();
            }
            if (!orgColumns.Contains("FinancialYearStartMonth"))
            {
                cmd.CommandText = "ALTER TABLE OrganizationSettings ADD COLUMN FinancialYearStartMonth INTEGER NOT NULL DEFAULT 1;";
                cmd.ExecuteNonQuery();
            }
            if (!orgColumns.Contains("DateFormat"))
            {
                cmd.CommandText = "ALTER TABLE OrganizationSettings ADD COLUMN DateFormat TEXT NOT NULL DEFAULT 'dd.MM.yyyy';";
                cmd.ExecuteNonQuery();
            }
            if (!orgColumns.Contains("TimeZoneId"))
            {
                cmd.CommandText = "ALTER TABLE OrganizationSettings ADD COLUMN TimeZoneId TEXT NOT NULL DEFAULT 'Europe/Minsk';";
                cmd.ExecuteNonQuery();
            }

            // Add ProjectId to Transactions if missing
            cmd.CommandText = "PRAGMA table_info('Transactions');";
            using var reader = cmd.ExecuteReader();
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                columns.Add(reader[1]?.ToString() ?? "");
            }
            reader.Close();

            if (!columns.Contains("ProjectId"))
            {
                cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN ProjectId INTEGER;";
                cmd.ExecuteNonQuery();
            }
            if (!columns.Contains("CreatedAt"))
            {
                cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN CreatedAt TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!columns.Contains("UpdatedAt"))
            {
                cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN UpdatedAt TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!columns.Contains("IsConfirmed"))
            {
                cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN IsConfirmed INTEGER NOT NULL DEFAULT 1;";
                cmd.ExecuteNonQuery();
            }
            if (!columns.Contains("PaymentMethod"))
            {
                cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN PaymentMethod INTEGER;";
                cmd.ExecuteNonQuery();
            }
            if (!columns.Contains("Counterparty"))
            {
                cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN Counterparty TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!columns.Contains("IsMandatory"))
            {
                cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN IsMandatory INTEGER NOT NULL DEFAULT 0;";
                cmd.ExecuteNonQuery();
            }
            if (!columns.Contains("CounterpartyId"))
            {
                cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN CounterpartyId INTEGER;";
                cmd.ExecuteNonQuery();
            }
            if (!columns.Contains("Notes"))
            {
                cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN Notes TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!columns.Contains("ApprovalStatus"))
            {
                cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN ApprovalStatus INTEGER NOT NULL DEFAULT 1;";
                cmd.ExecuteNonQuery();
            }
            if (!columns.Contains("SubmittedByUserId"))
            {
                cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN SubmittedByUserId TEXT;";
                cmd.ExecuteNonQuery();
            }

            // Add columns to AspNetUsers if missing
            cmd.CommandText = "PRAGMA table_info('AspNetUsers');";
            using var userReader = cmd.ExecuteReader();
            var userColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (userReader.Read())
            {
                userColumns.Add(userReader[1]?.ToString() ?? "");
            }
            userReader.Close();

            if (!userColumns.Contains("BaseCurrency"))
            {
                cmd.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN BaseCurrency TEXT NOT NULL DEFAULT 'BYN';";
                cmd.ExecuteNonQuery();
            }
            if (!userColumns.Contains("EnableNotifications"))
            {
                cmd.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN EnableNotifications INTEGER NOT NULL DEFAULT 1;";
                cmd.ExecuteNonQuery();
            }
            if (!userColumns.Contains("CreatedAt"))
            {
                cmd.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN CreatedAt TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!userColumns.Contains("NotificationDaysBefore"))
            {
                cmd.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN NotificationDaysBefore INTEGER NOT NULL DEFAULT 3;";
                cmd.ExecuteNonQuery();
            }

            // Add NotificationSentDate to Reminders if missing
            cmd.CommandText = "PRAGMA table_info('Reminders');";
            using var reminderReader = cmd.ExecuteReader();
            var reminderColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reminderReader.Read())
            {
                reminderColumns.Add(reminderReader[1]?.ToString() ?? "");
            }
            reminderReader.Close();

            if (!reminderColumns.Contains("NotificationSentDate"))
            {
                cmd.CommandText = "ALTER TABLE Reminders ADD COLUMN NotificationSentDate TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!reminderColumns.Contains("ReminderType"))
            {
                cmd.CommandText = "ALTER TABLE Reminders ADD COLUMN ReminderType INTEGER NOT NULL DEFAULT 5;";
                cmd.ExecuteNonQuery();
            }
            if (!reminderColumns.Contains("SnoozedUntil"))
            {
                cmd.CommandText = "ALTER TABLE Reminders ADD COLUMN SnoozedUntil TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!reminderColumns.Contains("IsArchived"))
            {
                cmd.CommandText = "ALTER TABLE Reminders ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0;";
                cmd.ExecuteNonQuery();
            }
            if (!reminderColumns.Contains("NotifyDaysBefore"))
            {
                cmd.CommandText = "ALTER TABLE Reminders ADD COLUMN NotifyDaysBefore INTEGER NOT NULL DEFAULT 3;";
                cmd.ExecuteNonQuery();
            }

            // Seed default categories if missing
            cmd.CommandText = "SELECT COUNT(1) FROM Categories;";
            var count = 0;
            using (var ccmd = connection.CreateCommand()) { ccmd.CommandText = "SELECT COUNT(1) FROM Categories;"; var r = ccmd.ExecuteScalar(); count = Convert.ToInt32(r); }
            if (count == 0)
            {
                foreach (var (name, type, keywords, icon, color) in CategoryDefaults.All)
                {
                    cmd.CommandText = "INSERT INTO Categories (Name, IsDefault, Type, Keywords, Icon, Color) VALUES (@name, 1, @type, @kw, @icon, @color);";
                    cmd.Parameters.Clear();
                    var pName = cmd.CreateParameter(); pName.ParameterName = "@name"; pName.Value = name; cmd.Parameters.Add(pName);
                    var pType = cmd.CreateParameter(); pType.ParameterName = "@type"; pType.Value = (int)type; cmd.Parameters.Add(pType);
                    var pKw = cmd.CreateParameter(); pKw.ParameterName = "@kw"; pKw.Value = string.IsNullOrWhiteSpace(keywords) ? DBNull.Value : keywords; cmd.Parameters.Add(pKw);
                    var pIcon = cmd.CreateParameter(); pIcon.ParameterName = "@icon"; pIcon.Value = icon; cmd.Parameters.Add(pIcon);
                    var pColor = cmd.CreateParameter(); pColor.ParameterName = "@color"; pColor.Value = color; cmd.Parameters.Add(pColor);
                    cmd.ExecuteNonQuery();
                }
            }

            // Ensure TaxPayments table exists
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS TaxPayments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Amount REAL NOT NULL DEFAULT 0,
                DueDate TEXT NOT NULL,
                IsPaid INTEGER NOT NULL DEFAULT 0,
                PaidDate TEXT,
                UserId TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_TaxPayments_UserId ON TaxPayments(UserId);";
            cmd.ExecuteNonQuery();

            // Add NotificationSentDate to TaxPayments if missing
            cmd.CommandText = "PRAGMA table_info('TaxPayments');";
            using var taxReader = cmd.ExecuteReader();
            var taxColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (taxReader.Read())
            {
                taxColumns.Add(taxReader[1]?.ToString() ?? "");
            }
            taxReader.Close();

            if (!taxColumns.Contains("NotificationSentDate"))
            {
                cmd.CommandText = "ALTER TABLE TaxPayments ADD COLUMN NotificationSentDate TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!taxColumns.Contains("PaidAmount"))
            {
                cmd.CommandText = "ALTER TABLE TaxPayments ADD COLUMN PaidAmount REAL NOT NULL DEFAULT 0;";
                cmd.ExecuteNonQuery();
            }
            if (!taxColumns.Contains("ReceiptNote"))
            {
                cmd.CommandText = "ALTER TABLE TaxPayments ADD COLUMN ReceiptNote TEXT;";
                cmd.ExecuteNonQuery();
            }

            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS TaxAutoRules (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                Name TEXT NOT NULL,
                PaymentName TEXT NOT NULL,
                Formula TEXT NOT NULL,
                Period INTEGER NOT NULL DEFAULT 1,
                DueDayOfMonth INTEGER NOT NULL DEFAULT 25,
                DueMonthOffset INTEGER NOT NULL DEFAULT 1,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_TaxAutoRules_UserId ON TaxAutoRules(UserId);";
            cmd.ExecuteNonQuery();

            // Ensure OrganizationSettings table exists
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS OrganizationSettings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                CompanyName TEXT NOT NULL DEFAULT '',
                UNP TEXT NOT NULL DEFAULT '',
                TaxSystem INTEGER NOT NULL DEFAULT 0
            );";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_OrganizationSettings_UserId ON OrganizationSettings(UserId);";
            cmd.ExecuteNonQuery();

            // Ensure Employees table exists
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Employees (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                FullName TEXT NOT NULL DEFAULT '',
                Position TEXT NOT NULL DEFAULT '',
                Email TEXT NOT NULL DEFAULT '',
                Phone TEXT NOT NULL DEFAULT '',
                Salary REAL NOT NULL DEFAULT 0,
                HireDate TEXT NOT NULL DEFAULT (date('now')),
                TerminationDate TEXT,
                IsActive INTEGER NOT NULL DEFAULT 1,
                Role TEXT NOT NULL DEFAULT 'Employee'
            );";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Employees_UserId ON Employees(UserId);";
            cmd.ExecuteNonQuery();

            if (!columns.Contains("CounterpartyId"))
            {
                cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN CounterpartyId INTEGER;";
                cmd.ExecuteNonQuery();
            }
            if (!columns.Contains("Notes"))
            {
                cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN Notes TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!columns.Contains("ApprovalStatus"))
            {
                cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN ApprovalStatus INTEGER NOT NULL DEFAULT 1;";
                cmd.ExecuteNonQuery();
            }
            if (!columns.Contains("SubmittedByUserId"))
            {
                cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN SubmittedByUserId TEXT;";
                cmd.ExecuteNonQuery();
            }

            if (!userColumns.Contains("ActiveProjectId"))
            {
                cmd.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN ActiveProjectId INTEGER;";
                cmd.ExecuteNonQuery();
            }
            if (!userColumns.Contains("Department"))
            {
                cmd.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN Department TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!userColumns.Contains("FirstName"))
            {
                cmd.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN FirstName TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!userColumns.Contains("LastName"))
            {
                cmd.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN LastName TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!userColumns.Contains("NotifyTaxes"))
            {
                cmd.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN NotifyTaxes INTEGER NOT NULL DEFAULT 1;";
                cmd.ExecuteNonQuery();
            }
            if (!userColumns.Contains("NotifyCashGaps"))
            {
                cmd.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN NotifyCashGaps INTEGER NOT NULL DEFAULT 1;";
                cmd.ExecuteNonQuery();
            }
            if (!userColumns.Contains("NotifyBills"))
            {
                cmd.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN NotifyBills INTEGER NOT NULL DEFAULT 1;";
                cmd.ExecuteNonQuery();
            }
            if (!userColumns.Contains("WorkspaceOwnerUserId"))
            {
                cmd.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN WorkspaceOwnerUserId TEXT;";
                cmd.ExecuteNonQuery();
            }

            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Tags (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, UserId TEXT NOT NULL, Name TEXT NOT NULL, Color TEXT);";
            cmd.ExecuteNonQuery();
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS TransactionTags (
                TransactionId INTEGER NOT NULL, TagId INTEGER NOT NULL, PRIMARY KEY (TransactionId, TagId));";
            cmd.ExecuteNonQuery();
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS TransactionAttachments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, TransactionId INTEGER NOT NULL, UserId TEXT NOT NULL,
                FileName TEXT NOT NULL, StoredPath TEXT NOT NULL, ContentType TEXT NOT NULL,
                FileSize INTEGER NOT NULL, UploadedAt TEXT NOT NULL);";
            cmd.ExecuteNonQuery();
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS TransactionComments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, TransactionId INTEGER NOT NULL, UserId TEXT NOT NULL,
                AuthorName TEXT, Text TEXT NOT NULL, CreatedAt TEXT NOT NULL);";
            cmd.ExecuteNonQuery();
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Counterparties (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, UserId TEXT NOT NULL, Name TEXT NOT NULL,
                Type INTEGER NOT NULL DEFAULT 2, ContactPerson TEXT, Email TEXT, Phone TEXT,
                TaxId TEXT, Notes TEXT, LogoUrl TEXT, CreatedAt TEXT NOT NULL);";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "PRAGMA table_info('Counterparties');";
            using (var cpReader = cmd.ExecuteReader())
            {
                var cpCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (cpReader.Read()) cpCols.Add(cpReader.GetString(1));
                cpReader.Close();
                if (!cpCols.Contains("LogoUrl"))
                {
                    cmd.CommandText = "ALTER TABLE Counterparties ADD COLUMN LogoUrl TEXT;";
                    cmd.ExecuteNonQuery();
                }
            }
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Debts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, UserId TEXT NOT NULL, Type INTEGER NOT NULL,
                CounterpartyName TEXT NOT NULL, CounterpartyId INTEGER, Amount REAL NOT NULL,
                PaidAmount REAL NOT NULL DEFAULT 0, DueDate TEXT, Description TEXT,
                IsSettled INTEGER NOT NULL DEFAULT 0, CreatedAt TEXT NOT NULL);";
            cmd.ExecuteNonQuery();
        }

        // Seed default categories if missing
        try
        {
            using var scope2 = app.Services.CreateScope();
            var db = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!db.Categories.Any())
            {
                var defaults = new[]
                {
                    new Category { Name = "Налоги", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "Аренда", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "Зарплата", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "Реклама", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "Продукты", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "Канцелярия", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "Транспорт", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "Интернет", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "Связь", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "Доход", IsDefault = true, Type = CategoryType.Income }
                };
                db.Categories.AddRange(defaults);
                db.SaveChanges();
            }
        }
        catch { }

        if (connection is Microsoft.Data.Sqlite.SqliteConnection sqliteConn)
            DatabaseSchemaRepair.ApplySchema(sqliteConn);

        DatabaseSchemaRepair.RepairDataAsync(dbContext, categorization).GetAwaiter().GetResult();

        connection.Close();
    }
    catch (Exception ex)
    {
        var log = scope.ServiceProvider.GetService<ILogger<Program>>();
        log?.LogWarning(ex, "Database schema migration failed");
    }
}

app.MapPost("/api/transactions/{id:int}/attachments", async (
    int id, HttpContext http, UserManager<ApplicationUser> userManager, IAttachmentService attachments) =>
{
    var user = await userManager.GetUserAsync(http.User);
    if (user == null) return Results.Unauthorized();
    var file = http.Request.Form.Files.FirstOrDefault();
    if (file == null || file.Length == 0) return Results.BadRequest("Файл не передан.");
    try
    {
        await using var stream = file.OpenReadStream();
        var result = await attachments.UploadAsync(id, user.Id, stream, file.FileName, file.ContentType);
        return Results.Ok(new { result.Id, result.FileName, url = attachments.GetPublicUrl(result) });
    }
    catch (Exception ex) { return Results.BadRequest(ex.Message); }
}).RequireAuthorization();

try { await RoleSeedService.SeedAsync(app.Services); } catch { }

app.MapGet("/api/account/export", async (
    HttpContext http,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db) =>
{
    var user = await userManager.GetUserAsync(http.User);
    if (user == null) return Results.Unauthorized();

    var payload = new
    {
        exportedAt = DateTime.UtcNow,
        user = new { user.Email, user.FirstName, user.LastName, user.BaseCurrency },
        transactions = await db.Transactions.Where(t => t.UserId == user.Id).ToListAsync(),
        categories = await db.Categories.ToListAsync(),
        reminders = await db.Reminders.Where(r => r.UserId == user.Id).ToListAsync(),
        taxes = await db.TaxPayments.Where(t => t.UserId == user.Id).ToListAsync(),
        projects = await db.Projects.Where(p => p.UserId == user.Id).ToListAsync(),
        debts = await db.Debts.Where(d => d.UserId == user.Id).ToListAsync()
    };

    return Results.Json(payload);
}).RequireAuthorization();

if (seedDiplomaDemo)
{
    await DiplomaDemoSeedService.RunAsync(app.Services);
    return;
}

app.Run();
