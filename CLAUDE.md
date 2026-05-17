# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MiniFinance — платформа учёта расходов малого бизнеса на ASP.NET Core Blazor Server (.NET 9.0).

**7 ключевых функций:**
1. Автоматический импорт транзакций из банков и платёжных систем (CSV)
2. Категоризация расходов (налоги, аренда, зарплаты)
3. Финансовые отчёты (P&L, cash flow)
4. Напоминания о сроке уплаты налогов и счетов
5. Анализ рентабельности проектов или отделов
6. Интеграция с облачными бухгалтерскими сервисами
7. Прогнозирование кассовых разрывов

## Development Commands

### Build and Run
```bash
dotnet build
dotnet run
dotnet run --launch-profile https
```

HTTP: http://localhost:5210 | HTTPS: https://localhost:7275

### Database Management

SQLite + EF Core, без миграций. `EnsureCreated()` + raw SQL в `Program.cs`.

Для сброса:
```bash
rm app.db && dotnet run
```

## Architecture

### Data Layer (`Data/Models/`)

- **ApplicationUser**: Identity user с `Transactions`, `BaseCurrency`, `EnableNotifications`, `CreatedAt`
- **Transaction**: Date, Amount, Description, Category, UserId, ProjectId, PaymentMethod, Counterparty, IsMandatory
- **Category**: Name (unique), Type (Income/Expense), IsDefault, Icon, Color
- **Project**: Name (unique), Status, Priority, Budget, ROI, ProjectManager, KPI, Risks
- **Reminder**: Name, Amount, Category, Frequency (OneTime/Monthly/Yearly), Date, IsPaid

**ApplicationDbContext**: DbSets — Transactions, Categories, Reminders, Projects.

Все запросы фильтруются по `UserId`.

### Services Layer (`Services/`)

| Сервис | Назначение |
|---|---|
| `ICsvParser / CsvParser` | Парсинг CSV для импорта транзакций |
| `ICategorizationService / CategorizationService` | Авто-категоризация по ключевым словам |
| `IReportService / ReportService` | Category breakdown, monthly trends, cashflow, project reports |
| `IForecastingService / ForecastingService` | Прогнозы income/expense, cashflow forecast |

### UI Layer (`Components/Pages/`)

| Страница | Назначение |
|---|---|
| `Home.razor` | Дашборд с KPI, прогнозами, напоминаниями |
| `Transactions.razor` | CRUD транзакций с фильтрами и авто-категоризацией |
| `Import.razor` | CSV импорт транзакций |
| `Categories.razor` | Управление категориями |
| `Reminders.razor` | Напоминания о платежах |
| `Projects.razor` | Аналитика проектов |
| `Reports.razor` | Финансовые отчёты с CSV/Excel экспортом |
| `Insights.razor` | Прогнозы и тренды |
| `Account.razor` | Профиль пользователя |

### Export Endpoints

- `/reports/export/csv` — CSV экспорт
- `/reports/export/xlsx` — Excel (.xlsx) через ClosedXML
- `/reports/export/excel` — HTML table (legacy)

Все требуют аутентификации, фильтруют по `UserId`. Параметры: `start`, `end`, `tab`, `projectId`.

## CSV Import Format

```csv
Date,Amount,Description,Category
2024-01-15,-1500.00,Аренда офиса,Аренда
```

Amount: положительная = доход, отрицательная = расход. Category опциональна (авто-категоризация).

## Configuration

- **Connection String**: `appsettings.json` → `"DataSource=app.db"`
- **Identity**: relaxed passwords (min 3 chars, no complexity)
- **Dependencies**: ClosedXML 0.105.0, EF Core 9.0, ASP.NET Core Identity 9.0
