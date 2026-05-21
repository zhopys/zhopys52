using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;
using MiniFinance.Services;
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
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddErrorDescriber<RussianIdentityErrorDescriber>();

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
builder.Services.AddScoped<ITeamService, TeamService>(); // UI РѕС‚РєР»СЋС‡РµРЅРѕ; СЃРµСЂРІРёСЃ РѕСЃС‚Р°РІР»РµРЅ РґР»СЏ Р±СѓРґСѓС‰РµРіРѕ РІРєР»СЋС‡РµРЅРёСЏ
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

// Legacy export URLs redirect to unified API
app.MapGet("/reports/export/csv", (HttpContext http) => ReportExportRedirects.RedirectToUnifiedExport(http, "csv")).RequireAuthorization("CanViewReports");
app.MapGet("/reports/export/xlsx", (HttpContext http) => ReportExportRedirects.RedirectToUnifiedExport(http, "xlsx")).RequireAuthorization("CanViewReports");
app.MapGet("/reports/export/excel", (HttpContext http) => ReportExportRedirects.RedirectToUnifiedExport(http, "xlsx")).RequireAuthorization("CanViewReports");
app.MapGet("/reports/export/pdf", (HttpContext http) => ReportExportRedirects.RedirectToUnifiedExport(http, "pdf")).RequireAuthorization("CanViewReports");

// Reports API
var reportsApi = app.MapGroup("/api/reports").RequireAuthorization("CanViewReports");


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
}).RequireAuthorization();

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
            // Ensure Projects table exists (INTEGER Status вЂ” matches EF enum)
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
                    new Category { Name = "РќР°Р»РѕРіРё", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "РђСЂРµРЅРґР°", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "Р—Р°СЂРїР»Р°С‚Р°", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "Р РµРєР»Р°РјР°", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "РџСЂРѕРґСѓРєС‚С‹", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "РљР°РЅС†РµР»СЏСЂРёСЏ", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "РўСЂР°РЅСЃРїРѕСЂС‚", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "РРЅС‚РµСЂРЅРµС‚", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "РЎРІСЏР·СЊ", IsDefault = true, Type = CategoryType.Expense },
                    new Category { Name = "Р”РѕС…РѕРґ", IsDefault = true, Type = CategoryType.Income }
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
    if (file == null || file.Length == 0) return Results.BadRequest("Р¤Р°Р№Р» РЅРµ РїРµСЂРµРґР°РЅ.");
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
