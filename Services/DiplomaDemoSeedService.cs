using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

/// <summary>
/// Полный сброс БД и демо-данные для защиты диплома / курсовой.
/// Запуск: dotnet run -- --seed-demo  (или --seed-diploma-demo)
/// </summary>
public static class DiplomaDemoSeedService
{
    public const string TargetEmail = "1238606@mtp.by";
    public const string AccountantEmail = "buhgalter@demo.local";
    public const string TaxSpecialistEmail = "nalog@demo.local";
    /// <summary>Пароль только если пользователь создаётся заново.</summary>
    public const string DefaultPassword = "Demo1234!";
    public const string TeamDemoPassword = "Demo1234!";

    public static async Task RunAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var categorization = scope.ServiceProvider.GetRequiredService<ICategorizationService>();

        await categorization.EnsureDefaultCategoriesAsync();
        await ClearBusinessDataAsync(db);

        var user = await userManager.FindByEmailAsync(TargetEmail);
        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = TargetEmail,
                Email = TargetEmail,
                EmailConfirmed = true,
                FirstName = "Матвей",
                LastName = "Демо",
                BaseCurrency = "BYN",
                EnableNotifications = true,
                NotifyTaxes = true,
                NotifyCashGaps = true,
                NotifyBills = true,
                CreatedAt = DateTime.UtcNow
            };
            var created = await userManager.CreateAsync(user, DefaultPassword);
            if (!created.Succeeded)
                throw new InvalidOperationException("Не удалось создать пользователя: " + string.Join("; ", created.Errors.Select(e => e.Description)));
            Console.WriteLine($"Создан пользователь {TargetEmail}, пароль: {DefaultPassword}");
        }
        else
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var reset = await userManager.ResetPasswordAsync(user, token, DefaultPassword);
            if (reset.Succeeded)
                Console.WriteLine($"Пароль администратора {TargetEmail} сброшен на: {DefaultPassword}");
            else
                Console.WriteLine($"Сохранён {TargetEmail} (пароль не изменён: {string.Join("; ", reset.Errors.Select(e => e.Description))})");
        }

        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        var roles = await userManager.GetRolesAsync(user);
        if (!roles.Contains(AppRoles.Administrator))
            await userManager.AddToRoleAsync(user, AppRoles.Administrator);

        await userManager.SetTwoFactorEnabledAsync(user, false);

        await SeedTeamMembersAsync(userManager, user.Id);
        await DeleteOtherUsersAsync(userManager, user.Id);
        await SeedDemoDataAsync(db, user.Id);

        Console.WriteLine();
        Console.WriteLine("=== Учётные записи для сдачи ===");
        Console.WriteLine($"  Администратор:  {TargetEmail}  /  {DefaultPassword}");
        Console.WriteLine($"  Бухгалтер:      {AccountantEmail}  /  {TeamDemoPassword}");
        Console.WriteLine($"  Налоговый:      {TaxSpecialistEmail}  /  {TeamDemoPassword}");
        Console.WriteLine();
        Console.WriteLine("Демо-данные загружены успешно.");
    }

    private static async Task SeedTeamMembersAsync(UserManager<ApplicationUser> userManager, string ownerUserId)
    {
        await EnsureTeamUserAsync(userManager, ownerUserId, AccountantEmail, "Анна", "Бухгалтерова",
            "Бухгалтерия", AppRoles.Accountant);
        await EnsureTeamUserAsync(userManager, ownerUserId, TaxSpecialistEmail, "Игорь", "Налогов",
            "Налоги", AppRoles.TaxSpecialist);
    }

    private static async Task EnsureTeamUserAsync(
        UserManager<ApplicationUser> userManager,
        string ownerUserId,
        string email,
        string firstName,
        string lastName,
        string department,
        string role)
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing != null)
        {
            if (existing.WorkspaceOwnerUserId != ownerUserId)
            {
                existing.WorkspaceOwnerUserId = ownerUserId;
                await userManager.UpdateAsync(existing);
            }
            var roles = await userManager.GetRolesAsync(existing);
            if (!roles.Contains(role))
            {
                await userManager.RemoveFromRolesAsync(existing, roles);
                await userManager.AddToRoleAsync(existing, role);
            }
            await userManager.SetTwoFactorEnabledAsync(existing, false);
            Console.WriteLine($"  Команда: {email} (уже есть)");
            return;
        }

        var member = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = firstName,
            LastName = lastName,
            Department = department,
            WorkspaceOwnerUserId = ownerUserId,
            BaseCurrency = "BYN",
            CreatedAt = DateTime.UtcNow
        };
        var created = await userManager.CreateAsync(member, TeamDemoPassword);
        if (!created.Succeeded)
            throw new InvalidOperationException($"Не удалось создать {email}: " + string.Join("; ", created.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(member, role);
        await userManager.SetTwoFactorEnabledAsync(member, false);
        Console.WriteLine($"  Команда: {email} / {TeamDemoPassword}");
    }

    private static async Task ClearBusinessDataAsync(ApplicationDbContext db)
    {
        await db.TransactionComments.ExecuteDeleteAsync();
        await db.TransactionAttachments.ExecuteDeleteAsync();
        await db.TransactionTags.ExecuteDeleteAsync();
        await db.Transactions.ExecuteDeleteAsync();
        await db.Debts.ExecuteDeleteAsync();
        await db.Counterparties.ExecuteDeleteAsync();
        await db.Reminders.ExecuteDeleteAsync();
        await db.TaxPayments.ExecuteDeleteAsync();
        await db.TaxAutoRules.ExecuteDeleteAsync();
        await db.OrganizationSettings.ExecuteDeleteAsync();
        await db.Projects.ExecuteDeleteAsync();
        await db.Tags.ExecuteDeleteAsync();

        await db.Database.ExecuteSqlRawAsync("DELETE FROM BankStatements;");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Employees;");
    }

    private static async Task DeleteOtherUsersAsync(UserManager<ApplicationUser> userManager, string keepUserId)
    {
        var toRemove = await userManager.Users
            .Where(x => x.Id != keepUserId && x.WorkspaceOwnerUserId != keepUserId)
            .ToListAsync();
        foreach (var u in toRemove)
            await userManager.DeleteAsync(u);
    }

    private static async Task SeedDemoDataAsync(ApplicationDbContext db, string userId)
    {
        var today = DateTime.Today;

        await InsertOrganizationSettingsAsync(db, userId);

        var projects = new[]
        {
            new Project
            {
                UserId = userId, Name = "CRM для логистики", IsDefault = false,
                Status = ProjectStatus.Active, Priority = ProjectPriority.High,
                Budget = 48000m, TargetROI = 35m,
                StartDate = today.AddMonths(-8), EndDate = today.AddMonths(2),
                ProjectManager = "М. Демо", Department = "Разработка",
                KPI = "Сдача MVP до 30.06, NPS клиента ≥ 8",
                Risks = "Задержка интеграции с 1С",
                Description = "Дипломный кейс: учёт проекта и маржинальности"
            },
            new Project
            {
                UserId = userId, Name = "Редизайн корпоративного сайта", IsDefault = false,
                Status = ProjectStatus.Active, Priority = ProjectPriority.Medium,
                Budget = 15000m, TargetROI = 25m,
                StartDate = today.AddMonths(-3), EndDate = today.AddMonths(1),
                ProjectManager = "М. Демо", Department = "Маркетинг",
                KPI = "Конверсия заявок +15%"
            },
            new Project
            {
                UserId = userId, Name = "Общий учёт", IsDefault = true,
                Status = ProjectStatus.Active, Priority = ProjectPriority.Low,
                Budget = null, Description = "Операционные расходы без привязки к проекту"
            }
        };
        db.Projects.AddRange(projects);
        await db.SaveChangesAsync();

        var crmId = projects[0].Id;
        var webId = projects[1].Id;
        var generalId = projects[2].Id;

        var counterparties = new[]
        {
            new CounterpartyRecord { UserId = userId, Name = "ООО «БелЛогистик»", Type = CounterpartyType.Client, ContactPerson = "Иванова А.", Email = "a.ivanova@bellog.by", Phone = "+375 29 111-22-33", TaxId = "101234567", LogoUrl = "https://ui-avatars.com/api/?name=BL&background=2563eb&color=fff&size=64" },
            new CounterpartyRecord { UserId = userId, Name = "ИП Козлов", Type = CounterpartyType.Client, ContactPerson = "Козлов П.С.", Phone = "+375 33 444-55-66", LogoUrl = "https://ui-avatars.com/api/?name=PK&background=22c55e&color=fff&size=64" },
            new CounterpartyRecord { UserId = userId, Name = "БЦ «Победа»", Type = CounterpartyType.Supplier, ContactPerson = "Аренда офиса", Phone = "+375 17 200-00-00", LogoUrl = "https://ui-avatars.com/api/?name=BC&background=f97316&color=fff&size=64" },
            new CounterpartyRecord { UserId = userId, Name = "A1 / МТС", Type = CounterpartyType.Supplier, ContactPerson = "Связь", LogoUrl = "https://ui-avatars.com/api/?name=A1&background=8b5cf6&color=fff&size=64" },
            new CounterpartyRecord { UserId = userId, Name = "Hoster.by", Type = CounterpartyType.Supplier, Email = "billing@hoster.by", LogoUrl = "https://ui-avatars.com/api/?name=HB&background=06b6d4&color=fff&size=64" },
            new CounterpartyRecord { UserId = userId, Name = "Яндекс Директ", Type = CounterpartyType.Supplier, LogoUrl = "https://ui-avatars.com/api/?name=YD&background=ef4444&color=fff&size=64" }
        };
        db.Counterparties.AddRange(counterparties);
        await db.SaveChangesAsync();

        var belLog = counterparties[0];
        var kozlov = counterparties[1];
        var rent = counterparties[2];
        var hoster = counterparties[4];

        db.TaxAutoRules.AddRange(
            new TaxAutoRule { UserId = userId, Name = "УСН 6%", PaymentName = "УСН", Formula = "income * 0.06", Period = TaxRulePeriod.Quarterly, DueDayOfMonth = 25, DueMonthOffset = 1, IsEnabled = true, SortOrder = 1 },
            new TaxAutoRule { UserId = userId, Name = "ФСЗН (оценка)", PaymentName = "ФСЗН", Formula = "income * 0.35", Period = TaxRulePeriod.Monthly, DueDayOfMonth = 20, DueMonthOffset = 1, IsEnabled = true, SortOrder = 2 }
        );

        var qEnd = new DateTime(today.Year, ((today.Month - 1) / 3 + 1) * 3, 1).AddMonths(1).AddDays(-1);
        db.TaxPayments.AddRange(
            new TaxPayment { UserId = userId, Name = "УСН Q1 " + today.Year, Amount = 2840m, DueDate = new DateTime(today.Year, 4, 25), IsPaid = true, PaidDate = new DateTime(today.Year, 4, 22), PaidAmount = 2840m },
            new TaxPayment { UserId = userId, Name = "УСН Q2 " + today.Year, Amount = 3120m, DueDate = new DateTime(today.Year, 7, 25) },
            new TaxPayment { UserId = userId, Name = "ФСЗН " + today.ToString("MMM yyyy"), Amount = 1850m, DueDate = today.AddDays(12) },
            new TaxPayment { UserId = userId, Name = "УСН (просрочено)", Amount = 980m, DueDate = today.AddDays(-18), PaidAmount = 0 },
            new TaxPayment { UserId = userId, Name = "УСН Q1 " + (today.Year - 1), Amount = 2650m, DueDate = today.AddMonths(-2), IsPaid = true, PaidDate = today.AddMonths(-2).AddDays(-3), PaidAmount = 2650m }
        );

        db.Reminders.AddRange(
            new Reminder { UserId = userId, Name = "Аренда офиса", Amount = 3500m, Category = "Аренда", Frequency = ReminderFrequency.Monthly, Date = new DateTime(today.Year, today.Month, 5), ReminderType = ReminderType.Rent, ProjectId = generalId },
            new Reminder { UserId = userId, Name = "УСН за квартал", Amount = 3120m, Category = "Налоги", Frequency = ReminderFrequency.Quarterly, Date = new DateTime(today.Year, 7, 25), ReminderType = ReminderType.Tax },
            new Reminder { UserId = userId, Name = "Зарплата команды", Amount = 24000m, Category = "Зарплата", Frequency = ReminderFrequency.Monthly, Date = new DateTime(today.Year, today.Month, 10), ReminderType = ReminderType.Salary },
            new Reminder { UserId = userId, Name = "Счёт Hoster.by", Amount = 89m, Category = "Хостинг", Frequency = ReminderFrequency.OneTime, Date = today.AddDays(18), ReminderType = ReminderType.Bill },
            new Reminder { UserId = userId, Name = "Подписка Figma", Amount = 45m, Category = "Офисные расходы", Frequency = ReminderFrequency.Monthly, Date = today.AddDays(7), ReminderType = ReminderType.Subscription, IsPaid = true, PaidDate = today.AddDays(-2) }
        );

        db.Debts.AddRange(
            new Debt { UserId = userId, Type = DebtType.Receivable, CounterpartyName = belLog.Name, CounterpartyId = belLog.Id, Amount = 8500m, PaidAmount = 3000m, DueDate = today.AddDays(14), Description = "Остаток по этапу 2 CRM" },
            new Debt { UserId = userId, Type = DebtType.Payable, CounterpartyName = "ООО «Поставка ПО»", Amount = 4200m, PaidAmount = 0, DueDate = today.AddDays(21), Description = "Лицензии на разработку" }
        );

        var tagUrgent = new Tag { UserId = userId, Name = "срочно", Color = "#ef4444" };
        var tagProject = new Tag { UserId = userId, Name = "проект", Color = "#3b82f6" };
        db.Tags.AddRange(tagUrgent, tagProject);
        await db.SaveChangesAsync();

        var transactions = BuildTransactions(userId, today, crmId, webId, generalId, belLog, kozlov, rent, hoster);
        db.Transactions.AddRange(transactions);

        db.Transactions.AddRange(
            new Transaction
            {
                UserId = userId, Date = today.AddDays(-2), Amount = -2400m,
                Description = "Закупка ноутбука для разработки", Category = "Закупки",
                ProjectId = crmId, Counterparty = "ООО «Поставка ПО»", PaymentMethod = PaymentMethod.Card,
                IsConfirmed = false, ApprovalStatus = TransactionApprovalStatus.Pending,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new Transaction
            {
                UserId = userId, Date = today.AddDays(-1), Amount = 12000m,
                Description = "Аванс по CRM — ожидает утверждения", Category = "Доход от услуг",
                ProjectId = crmId, Counterparty = belLog.Name, CounterpartyId = belLog.Id,
                IsConfirmed = false, ApprovalStatus = TransactionApprovalStatus.Pending,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new Transaction
            {
                UserId = userId, Date = today, Amount = -890m,
                Description = "Командировочные (чек приложен)", Category = "Прочее",
                ProjectId = webId, IsConfirmed = false, ApprovalStatus = TransactionApprovalStatus.Pending,
                CreatedAt = DateTime.UtcNow
            }
        );

        await db.SaveChangesAsync();

        var txWithTags = transactions.Where(t => t.Description.Contains("CRM") || t.Description.Contains("этап")).Take(4).ToList();
        foreach (var tx in txWithTags)
        {
            db.TransactionTags.Add(new TransactionTag { TransactionId = tx.Id, TagId = tagProject.Id });
            if (tx.Amount > 10000)
                db.TransactionTags.Add(new TransactionTag { TransactionId = tx.Id, TagId = tagUrgent.Id });
        }

        var bigTx = transactions.First(t => t.Amount >= 15000);
        db.TransactionComments.Add(new TransactionComment
        {
            TransactionId = bigTx.Id,
            UserId = userId,
            Text = "Согласовано с клиентом, акт подписан.",
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        });

        await db.SaveChangesAsync();

        var user = await db.Users.FindAsync(userId);
        if (user != null)
        {
            user.ActiveProjectId = crmId;
            user.Department = "Финансы";
            user.FirstName ??= "Матвей";
            user.LastName ??= "Демо";
            await db.SaveChangesAsync();
        }

        Console.WriteLine($"  Проектов: {await db.Projects.CountAsync(p => p.UserId == userId)}");
        Console.WriteLine($"  Транзакций: {await db.Transactions.CountAsync(t => t.UserId == userId)}");
        Console.WriteLine($"  Контрагентов: {await db.Counterparties.CountAsync(c => c.UserId == userId)}");
        Console.WriteLine($"  Налогов: {await db.TaxPayments.CountAsync(t => t.UserId == userId)}");
        Console.WriteLine($"  На утверждении: {await db.Transactions.CountAsync(t => t.UserId == userId && t.ApprovalStatus == TransactionApprovalStatus.Pending)}");
        Console.WriteLine($"  Напоминаний: {await db.Reminders.CountAsync(r => r.UserId == userId)}");
    }

    private static async Task InsertOrganizationSettingsAsync(ApplicationDbContext db, string userId)
    {
        var settings = new OrganizationSettings
        {
            UserId = userId,
            OrganizationId = userId,
            CompanyName = "ООО «ТехноСтарт»",
            UNP = "193456789",
            TaxSystem = TaxSystem.USN,
            MinCashBalance = 5000m,
            WeekStartsOn = 1,
            FinancialYearStartMonth = 1
        };

        try
        {
            db.OrganizationSettings.Add(settings);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("OrganizationId", StringComparison.OrdinalIgnoreCase) == true)
        {
            db.ChangeTracker.Clear();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO OrganizationSettings (UserId, OrganizationId, CompanyName, UNP, TaxSystem, MinCashBalance, WeekStartsOn, FinancialYearStartMonth, DateFormat, TimeZoneId)
                  VALUES ({0}, {0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})",
                userId, settings.CompanyName, settings.UNP, (int)settings.TaxSystem, settings.MinCashBalance,
                settings.WeekStartsOn, settings.FinancialYearStartMonth, settings.DateFormat, settings.TimeZoneId);
        }
    }

    private static List<Transaction> BuildTransactions(
        string userId, DateTime today, int crmId, int webId, int generalId,
        CounterpartyRecord belLog, CounterpartyRecord kozlov, CounterpartyRecord rent, CounterpartyRecord hoster)
    {
        var list = new List<Transaction>();
        var start = today.AddMonths(-9);
        var rnd = new Random(42);

        void Add(DateTime date, decimal amount, string desc, string cat, int? projectId = null,
            CounterpartyRecord? cp = null, PaymentMethod? pm = PaymentMethod.BankTransfer, bool mandatory = false)
        {
            list.Add(new Transaction
            {
                UserId = userId,
                Date = date,
                Amount = amount,
                Description = desc,
                Category = cat,
                ProjectId = projectId,
                Counterparty = cp?.Name,
                CounterpartyId = cp?.Id,
                PaymentMethod = pm,
                IsMandatory = mandatory,
                IsConfirmed = true,
                ApprovalStatus = TransactionApprovalStatus.Approved,
                CreatedAt = date.ToUniversalTime(),
                Notes = amount < 0 && mandatory ? "Обязательный платёж" : null
            });
        }

        for (var m = 0; m < 9; m++)
        {
            var month = start.AddMonths(m);
            if (month > today) break;
            var y = month.Year;
            var mo = month.Month;
            DateTime OnDay(int d) => new(y, mo, Math.Min(d, DateTime.DaysInMonth(y, mo)));

            Add(OnDay(3), 18500m + m * 400, "Оплата этапа CRM — БелЛогистик", "Доход от услуг", crmId, belLog);
            if (m % 2 == 0)
                Add(OnDay(12), 6200m + rnd.Next(500), "Консультация и доработки — ИП Козлов", "Доход от услуг", webId, kozlov);
            if (m % 3 == 1)
                Add(OnDay(18), 9800m, "Предоплата по сайту", "Доход от услуг", webId, kozlov);

            Add(OnDay(5), -3500m, "Аренда офиса БЦ Победа", "Аренда", generalId, rent, mandatory: true);
            Add(OnDay(10), -12000m, "Зарплата разработчик (аванс)", "Зарплата", crmId, mandatory: true);
            Add(OnDay(25), -12000m, "Зарплата разработчик (выплата)", "Зарплата", crmId, mandatory: true);
            Add(OnDay(8), -89m, "Hoster.by VPS", "Хостинг", crmId, hoster);
            Add(OnDay(15), -450m - rnd.Next(100), "Яндекс Директ", "Реклама", webId);
            Add(OnDay(7), -120m, "Мобильная связь A1", "Связь", generalId);
            Add(OnDay(20), -380m, "Канцелярия и расходники", "Офисные расходы", generalId);

            if (m % 3 == 2)
                Add(OnDay(28), -(2600m + m * 80), "УСН за квартал", "Налоги", generalId, mandatory: true, pm: PaymentMethod.BankTransfer);

            if (m == 2)
                Add(OnDay(14), -5200m, "Закупка лицензий ПО", "Закупки", crmId);
            if (m == 5)
                Add(OnDay(6), 22000m, "Бонус за досрочную сдачу MVP", "Доход от услуг", crmId, belLog);
        }

        Add(today.AddDays(-3), -1500m, "Курьерская доставка документов", "Прочее", generalId);
        Add(today.AddDays(-1), 4500m, "Оплата счёта за поддержку", "Доход от услуг", crmId, belLog, PaymentMethod.Card);

        return list.OrderBy(t => t.Date).ToList();
    }
}
