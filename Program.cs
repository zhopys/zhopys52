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

// Core services
builder.Services.AddScoped<ICsvParser, CsvParser>();
builder.Services.AddScoped<ICategorizationService, CategorizationService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IForecastingService, ForecastingService>();
builder.Services.AddScoped<ITaxService, TaxService>();
builder.Services.AddScoped<MiniFinance.Components.Account.IdentityRedirectManager>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Identity.IEmailSender<ApplicationUser>, MiniFinance.Components.Account.IdentityNoOpEmailSender>();

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

    var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    var fileName = $"report_{tab ?? "all"}_{start:yyyyMMdd}_{end:yyyyMMdd}.csv";
    return Results.File(bytes, "text/csv", fileName);

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
        wsProj.Columns().AdjustToContents();
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

app.MapGet("/do-logout", async (HttpContext ctx, SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/login");
});

app.MapRazorComponents<MiniFinance.Components.App>()
    .AddInteractiveServerRenderMode()
    .DisableAntiforgery();

app.MapAdditionalIdentityEndpoints();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.EnsureCreated();

    try
    {
        var connection = dbContext.Database.GetDbConnection();
        connection.Open();

        using (var cmd = connection.CreateCommand())
        {
            // Ensure Projects table exists
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS Projects (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Description TEXT, Status TEXT NOT NULL DEFAULT 'Active', Priority INTEGER NOT NULL DEFAULT 1, Budget REAL, StartDate TEXT, EndDate TEXT, ProjectManager TEXT, ROI REAL, KPI TEXT, Risks TEXT, Notes TEXT, IsDefault INTEGER NOT NULL DEFAULT 0);";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_Projects_Name ON Projects(Name);";
            cmd.ExecuteNonQuery();

            // Add Department and TargetROI columns to Projects if missing
            cmd.CommandText = "PRAGMA table_info('Projects');";
            using var projReader = cmd.ExecuteReader();
            var projColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (projReader.Read())
            {
                projColumns.Add(projReader[1]?.ToString() ?? "");
            }
            projReader.Close();

            if (!projColumns.Contains("Department"))
            {
                cmd.CommandText = "ALTER TABLE Projects ADD COLUMN Department TEXT;";
                cmd.ExecuteNonQuery();
            }
            if (!projColumns.Contains("ROI"))
            {
                cmd.CommandText = "ALTER TABLE Projects ADD COLUMN ROI REAL;";
                cmd.ExecuteNonQuery();
            }

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

        connection.Close();
    }
    catch { }
}

app.Run();
