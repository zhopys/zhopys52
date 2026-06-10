using PuppeteerSharp;

var baseUrl = args.ElementAtOrDefault(0) ?? "http://localhost:5210";
var outDir = args.ElementAtOrDefault(1) ?? Path.Combine("docs", "pz", "screenshots");
var email = args.ElementAtOrDefault(2) ?? "1238606@mtp.by";
var password = args.ElementAtOrDefault(3) ?? "Demo1234!";

Directory.CreateDirectory(outDir);

var browserFetcher = new BrowserFetcher();
await browserFetcher.DownloadAsync();

await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
{
    Headless = true,
    Args = ["--no-sandbox", "--disable-dev-shm-usage", "--window-size=1440,900"]
});

var page = await browser.NewPageAsync();
await page.SetViewportAsync(new ViewPortOptions { Width = 1440, Height = 900 });

async Task Shot(string file, string url, int delayMs = 2500)
{
    await page.GoToAsync(url, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }, Timeout = 60000 });
    await Task.Delay(delayMs);
    await page.ScreenshotAsync(Path.Combine(outDir, file), new ScreenshotOptions { FullPage = false });
    Console.WriteLine($"OK {file}");
}

await Shot("2-01-login.png", $"{baseUrl}/login");
await Shot("2-01b-register.png", $"{baseUrl}/Account/Register");

await EnsureLoggedInAsync(page, baseUrl, email, password);

await Shot("2-02-home.png", $"{baseUrl}/");
await Shot("2-03-transactions.png", $"{baseUrl}/transactions");
await Shot("2-04-reports.png", $"{baseUrl}/reports?tab=pl");
await Shot("2-05-analytics.png", $"{baseUrl}/analytics");
await Shot("2-06-import.png", $"{baseUrl}/import");
await Shot("2-07-categories.png", $"{baseUrl}/categories");
await Shot("2-08-taxes.png", $"{baseUrl}/taxes");
await Shot("2-09-cashforecast.png", $"{baseUrl}/cash-forecast");
await Shot("2-10-reminders.png", $"{baseUrl}/reminders");

await page.GoToAsync($"{baseUrl}/login");
await Task.Delay(800);
await Shot("3-01-login.png", $"{baseUrl}/login", 400);

await EnsureLoggedInAsync(page, baseUrl, email, password);
await Shot("3-04-home.png", $"{baseUrl}/", 3000);
await Shot("3-05-transactions.png", $"{baseUrl}/transactions", 3000);
await Shot("3-13-import.png", $"{baseUrl}/import", 3000);
await Shot("3-16-categories.png", $"{baseUrl}/categories", 3000);
await Shot("3-25-reports-export.png", $"{baseUrl}/reports?tab=pl", 3000);
await Shot("3-27-analytics.png", $"{baseUrl}/analytics", 3000);

await page.GoToAsync($"{baseUrl}/login");
await Task.Delay(800);
await Shot("4-01-login.png", $"{baseUrl}/login", 400);

await EnsureLoggedInAsync(page, baseUrl, email, password);
await Shot("4-02-home.png", $"{baseUrl}/", 3000);
await Shot("4-03-transactions.png", $"{baseUrl}/transactions", 3000);
await Shot("4-05-settings.png", $"{baseUrl}/settings", 3000);
await Shot("4-07-analytics.png", $"{baseUrl}/analytics", 3000);

Console.WriteLine($"Screenshots saved to {Path.GetFullPath(outDir)}");

static async Task<bool> IsLoggedInAsync(IPage page)
{
    return await page.EvaluateFunctionAsync<bool>("() => !!document.querySelector('.app-shell')");
}

static async Task EnsureLoggedInAsync(IPage page, string baseUrl, string email, string password)
{
    await page.GoToAsync($"{baseUrl}/", new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }, Timeout = 60000 });
    await Task.Delay(1000);
    if (await IsLoggedInAsync(page))
    {
        Console.WriteLine("Already logged in");
        return;
    }

    await page.GoToAsync($"{baseUrl}/login", new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }, Timeout = 60000 });
    await page.WaitForSelectorAsync("#email");
    await page.EvaluateFunctionAsync(
        """(email, pwd) => { document.querySelector('#email').value = email; document.querySelector('#password').value = pwd; }""",
        email, password);

    var navTask = page.WaitForNavigationAsync(new NavigationOptions
    {
        WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
        Timeout = 60000
    });
    await page.ClickAsync("form button[type=submit]");
    await navTask;
    await page.WaitForSelectorAsync(".app-shell", new WaitForSelectorOptions { Timeout = 30000 });
    await Task.Delay(1500);
    Console.WriteLine($"Logged in, URL: {page.Url}");
}
