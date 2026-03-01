using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;
using MiniFinance.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = false;
        options.Password.RequiredLength = 3;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<ICsvParser, CsvParser>();
builder.Services.AddScoped<ICategorizationService, CategorizationService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IForecastingService, ForecastingService>();
builder.Services.AddScoped<MiniFinance.Components.Account.IdentityRedirectManager>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Identity.IEmailSender<ApplicationUser>, MiniFinance.Components.Account.IdentityNoOpEmailSender>();
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

// Export endpoints for reports (CSV and Excel). Protected.
app.MapGet("/reports/export/csv", async (HttpContext http, ApplicationDbContext db, UserManager<ApplicationUser> userManager, IReportService reportService) =>
{
    var user = await userManager.GetUserAsync(http.User);
    if (user == null) return Results.Unauthorized();

    var qs = http.Request.Query;
    DateTime.TryParse(qs["start"], out var start);
    DateTime.TryParse(qs["end"], out var end);
    var tab = qs["tab"].ToString();
    int.TryParse(qs["projectId"], out var projectId);

    // default range if parsing fails
    if (start == default) start = DateTime.Today.AddMonths(-1);
    if (end == default) end = DateTime.Today;

    var transactions = await db.Transactions
        .Include(t => t.Project)
        .Where(t => t.UserId == user.Id && t.Date >= start && t.Date <= end)
        .OrderBy(t => t.Date)
        .ToListAsync();

    var sb = new System.Text.StringBuilder();

    // Tab-specific CSV formats
    if (!string.IsNullOrEmpty(tab) && tab.Equals("projects", StringComparison.OrdinalIgnoreCase))
    {
        // optionally filter by projectId
        var tx = transactions;
        if (projectId > 0)
            tx = tx.Where(t => t.ProjectId == projectId).ToList();

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
    else // default: transactions list / cashflow
    {
        sb.AppendLine("Date,Description,Category,Amount,Project");
        foreach (var t in transactions)
        {
            string esc(string s) => '"' + (s ?? string.Empty).Replace("\"", "\"\"") + '"';
            sb.AppendLine($"{t.Date:yyyy-MM-dd},{esc(t.Description)},{esc(t.Category)},{t.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)},{esc(t.Project?.Name)}");
        }
    }

    var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    var fileName = $"report_{tab ?? "all"}_{start:yyyyMMdd}_{end:yyyyMMdd}.csv";
    return Results.File(bytes, "text/csv", fileName);

    static string Escape(string s) => '"' + (s ?? string.Empty).Replace("\"", "\"\"") + '"';
}).RequireAuthorization();

// Real .xlsx export using ClosedXML
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

    using var wb = new XLWorkbook();

    // If tab specified, produce only that sheet
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
        wsProj.Columns().AdjustToContents();
    }
    else if (!string.IsNullOrEmpty(tab) && tab.Equals("categories", StringComparison.OrdinalIgnoreCase))
    {
        var catReport = reportService.GetCategoryBreakdown(transactions);
        var wsCatExp = wb.Worksheets.Add("Categories Expense");
        wsCatExp.Cell(1, 1).Value = "Category";
        wsCatExp.Cell(1, 2).Value = "Amount";
        wsCatExp.Cell(1, 3).Value = "Count";
        wsCatExp.Cell(1, 4).Value = "Percentage";
        for (int i = 0; i < catReport.ExpenseByCategory.Count; i++)
        {
            var r = i + 2;
            var c = catReport.ExpenseByCategory[i];
            wsCatExp.Cell(r, 1).Value = c.Category;
            wsCatExp.Cell(r, 2).Value = c.Amount;
            wsCatExp.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            wsCatExp.Cell(r, 3).Value = c.Count;
            wsCatExp.Cell(r, 4).Value = (double)c.Percentage;
            wsCatExp.Cell(r, 4).Style.NumberFormat.Format = "0.0%";
        }
        wsCatExp.Columns().AdjustToContents();
    }
    else // default: include transactions + categories + projects + cashflow
    {
        // Transactions sheet
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
        ws.Columns().AdjustToContents();

        // Category report
        var catReport = reportService.GetCategoryBreakdown(transactions);
        var wsCat = wb.Worksheets.Add("Categories Income");
        wsCat.Cell(1, 1).Value = "Category";
        wsCat.Cell(1, 2).Value = "Amount";
        wsCat.Cell(1, 3).Value = "Count";
        wsCat.Cell(1, 4).Value = "Percentage";
        for (int i = 0; i < catReport.IncomeByCategory.Count; i++)
        {
            var r = i + 2;
            var c = catReport.IncomeByCategory[i];
            wsCat.Cell(r, 1).Value = c.Category;
            wsCat.Cell(r, 2).Value = c.Amount;
            wsCat.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            wsCat.Cell(r, 3).Value = c.Count;
            wsCat.Cell(r, 4).Value = (double)c.Percentage;
            wsCat.Cell(r, 4).Style.NumberFormat.Format = "0.0%";
        }
        wsCat.Columns().AdjustToContents();

        var wsCatExp = wb.Worksheets.Add("Categories Expense");
        wsCatExp.Cell(1, 1).Value = "Category";
        wsCatExp.Cell(1, 2).Value = "Amount";
        wsCatExp.Cell(1, 3).Value = "Count";
        wsCatExp.Cell(1, 4).Value = "Percentage";
        for (int i = 0; i < catReport.ExpenseByCategory.Count; i++)
        {
            var r = i + 2;
            var c = catReport.ExpenseByCategory[i];
            wsCatExp.Cell(r, 1).Value = c.Category;
            wsCatExp.Cell(r, 2).Value = c.Amount;
            wsCatExp.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            wsCatExp.Cell(r, 3).Value = c.Count;
            wsCatExp.Cell(r, 4).Value = (double)c.Percentage;
            wsCatExp.Cell(r, 4).Style.NumberFormat.Format = "0.0%";
        }
        wsCatExp.Columns().AdjustToContents();

        // Projects
        var projects = reportService.GetProjectReport(transactions);
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
        wsProj.Columns().AdjustToContents();

        // Cashflow
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
        wsCash.Columns().AdjustToContents();
    }

    using var ms = new System.IO.MemoryStream();
    wb.SaveAs(ms);
    ms.Position = 0;
    var fileName = $"report_{tab ?? "all"}_{start:yyyyMMdd}_{end:yyyyMMdd}.xlsx";
    return Results.File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);

}).RequireAuthorization();

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

    // Build a simple HTML table that Excel can open
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

app.MapRazorComponents<MiniFinance.Components.App>()
    .AddInteractiveServerRenderMode()
    .DisableAntiforgery();

app.MapAdditionalIdentityEndpoints();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // Ensure database exists
    dbContext.Database.EnsureCreated();

    // For lightweight schema updates (SQLite), add missing columns/tables if necessary.
    try
    {
        var connection = dbContext.Database.GetDbConnection();
        connection.Open();

        using (var cmd = connection.CreateCommand())
        {
            // Ensure Projects table exists
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS Projects (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Description TEXT, IsDefault INTEGER NOT NULL DEFAULT 0);";
            cmd.ExecuteNonQuery();

            // Ensure unique index on Projects.Name
            cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_Projects_Name ON Projects(Name);";
            cmd.ExecuteNonQuery();

            // Check if Transactions.ProjectId column exists
            cmd.CommandText = "PRAGMA table_info('Transactions');";
            using var reader = cmd.ExecuteReader();
            var hasProjectId = false;
            while (reader.Read())
            {
                var colName = reader[1]?.ToString();
                if (string.Equals(colName, "ProjectId", StringComparison.OrdinalIgnoreCase))
                {
                    hasProjectId = true;
                    break;
                }
            }
            reader.Close();

            if (!hasProjectId)
            {
                // Add ProjectId column (nullable integer). Note: SQLite doesn't support adding FK constraints via ALTER TABLE.
                cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN ProjectId INTEGER;";
                cmd.ExecuteNonQuery();
            }
            
            // Ensure Reminders table exists
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS Reminders (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Amount REAL NOT NULL, Category TEXT, Frequency INTEGER NOT NULL DEFAULT 0, Date TEXT NOT NULL, IsPaid INTEGER NOT NULL DEFAULT 0, PaidDate TEXT, UserId TEXT NOT NULL);";
            cmd.ExecuteNonQuery();

            // Ensure index on Reminders.UserId
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Reminders_UserId ON Reminders(UserId);";
            cmd.ExecuteNonQuery();

            // Ensure Categories table exists
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS Categories (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Description TEXT, IsDefault INTEGER NOT NULL DEFAULT 0, Type INTEGER NOT NULL DEFAULT 0);";
            cmd.ExecuteNonQuery();

            // Ensure unique index on Categories.Name
            cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_Categories_Name ON Categories(Name);";
            cmd.ExecuteNonQuery();

            // Check if Categories.Type column exists (for older DBs)
            cmd.CommandText = "PRAGMA table_info('Categories');";
            using var reader2 = cmd.ExecuteReader();
            var hasType = false;
            while (reader2.Read())
            {
                var colName = reader2[1]?.ToString();
                if (string.Equals(colName, "Type", StringComparison.OrdinalIgnoreCase))
                {
                    hasType = true;
                    break;
                }
            }
            reader2.Close();
            if (!hasType)
            {
                cmd.CommandText = "ALTER TABLE Categories ADD COLUMN Type INTEGER NOT NULL DEFAULT 0;";
                cmd.ExecuteNonQuery();
            }
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
                new Category { Name = "Зарплата", IsDefault = true, Type = CategoryType.Income },
                new Category { Name = "Реклама", IsDefault = true, Type = CategoryType.Expense },
                new Category { Name = "Продукты", IsDefault = true, Type = CategoryType.Expense },
                new Category { Name = "Канцелярия", IsDefault = true, Type = CategoryType.Expense },
                new Category { Name = "Транспорт", IsDefault = true, Type = CategoryType.Expense },
                new Category { Name = "Интернет", IsDefault = true, Type = CategoryType.Expense },
                new Category { Name = "Связь", IsDefault = true, Type = CategoryType.Expense }
            };
            db.Categories.AddRange(defaults);
            db.SaveChanges();
        }
    }
    catch
    {
        // best-effort only
    }

        connection.Close();
    }
    catch
    {
        // Best-effort only — if this fails, developer should run EF migrations or recreate DB.
    }
}

app.Run();