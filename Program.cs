using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Services;
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
app.MapGet("/reports/export/csv", async (HttpContext http, ApplicationDbContext db, UserManager<ApplicationUser> userManager) =>
{
    var user = await userManager.GetUserAsync(http.User);
    if (user == null) return Results.Unauthorized();

    var qs = http.Request.Query;
    DateTime.TryParse(qs["start"], out var start);
    DateTime.TryParse(qs["end"], out var end);

    // default range if parsing fails
    if (start == default) start = DateTime.Today.AddMonths(-1);
    if (end == default) end = DateTime.Today;

    var transactions = await db.Transactions
        .Where(t => t.UserId == user.Id && t.Date >= start && t.Date <= end)
        .OrderBy(t => t.Date)
        .ToListAsync();

    var sb = new System.Text.StringBuilder();
    sb.AppendLine("Date,Description,Category,Amount,UserId");
    foreach (var t in transactions)
    {
        string esc(string s) => '"' + (s ?? string.Empty).Replace("\"", "\"\"") + '"';
        sb.AppendLine($"{t.Date:yyyy-MM-dd},{esc(t.Description)},{esc(t.Category)},{t.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)},{esc(t.UserId)}");
    }

    var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    var fileName = $"transactions_{start:yyyyMMdd}_{end:yyyyMMdd}.csv";
    return Results.File(bytes, "text/csv", fileName);
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
    dbContext.Database.EnsureCreated();
}

app.Run();