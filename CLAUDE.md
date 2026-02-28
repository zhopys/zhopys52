# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MiniFinance is a personal finance management application built with ASP.NET Core Blazor Server (.NET 10.0 preview). It provides multi-user transaction tracking with CSV import capabilities.

## Development Commands

### Build and Run
```bash
# Build the project
dotnet build

# Run the application
dotnet run

# Run with specific profile
dotnet run --launch-profile https
```

The app runs on:
- HTTP: http://localhost:5210
- HTTPS: https://localhost:7275

### Database Management

The application uses SQLite with Entity Framework Core. The database is automatically created on startup using `DbContext.Database.EnsureCreated()` in `Program.cs`.

**Important**: This project does NOT use EF migrations. Schema changes require:
1. Delete `Data/app.db`
2. Update entity models in `Data/`
3. Restart the application to recreate the database

To manually reset the database:
```bash
rm Data/app.db
dotnet run
```

## Architecture

### Data Layer (`Data/`)

- **ApplicationDbContext**: EF Core context inheriting from `IdentityDbContext<ApplicationUser>`
- **ApplicationUser**: Custom Identity user with navigation property to Transactions
- **Transaction**: Core entity with user isolation via `UserId` foreign key
- **Relationship**: One-to-many (User → Transactions) with cascade delete

All database queries must filter by `UserId` to ensure data isolation between users.

### Services Layer (`Services/`)

- **ICsvParser / CsvParser**: Parses CSV files for transaction import
- CSV format: `Date,Amount,Description,Category`
- Positive amounts = income, negative = expenses
- Default category: "Прочее" (Other)

### UI Layer (`Components/`)

**Pages** (`Components/Pages/`):
- `Home.razor`: Dashboard with statistics (total transactions, income, expenses, balance)
- `Transactions.razor`: CRUD interface for manual transaction management
- `Import.razor`: CSV file upload with validation (max 10MB)

**Authentication** (`Components/Account/`):
- Full ASP.NET Core Identity scaffolding (login, register, 2FA, password reset, etc.)
- Password requirements are relaxed for development (min 3 chars, no complexity)

**Layout** (`Components/Layout/`):
- `MainLayout.razor`: Main application layout
- `NavMenu.razor`: Navigation with authorization-based menu items

### Authentication & Authorization

All main pages use `@attribute [Authorize]` and `<AuthorizeView>` components. User context is accessed via:
```csharp
var user = await UserManager.GetUserAsync(context.User);
```

Always filter database queries by `user.Id` to maintain data isolation.

### Key Patterns

1. **User Data Isolation**: Every transaction query must include `.Where(t => t.UserId == user.Id)`
2. **Blazor Context**: Use `context.User` within `<AuthorizeView Context="authContext">` blocks
3. **Database Auto-Creation**: No migrations; database schema is created from entity models on startup
4. **CSV Import**: Handles malformed lines gracefully with try-catch, skips invalid rows

## CSV Import Format

Expected format for transaction imports:
```csv
Date,Amount,Description,Category
2024-01-15,-1500.00,Аренда офиса,Аренда
2024-01-20,5000.00,Зарплата,Доход
```

- Date: Any format parseable by `DateTime.Parse` with `InvariantCulture`
- Amount: Decimal with `InvariantCulture` (negative for expenses, positive for income)
- Description: Required text field
- Category: Optional (defaults to "Прочее")

## Configuration

- **Connection String**: Defined in `appsettings.json` as `"DataSource=app.db"`
- **User Secrets**: Configured with ID `MiniFinance-12345678`
- **Identity Options**: Configured in `Program.cs` with relaxed password requirements for development
