using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

/// <summary>
/// Дополняет схему SQLite (без EF-миграций) и выравнивает справочники с транзакциями.
/// </summary>
public static class DatabaseSchemaRepair
{
    public static void ApplySchema(SqliteConnection connection, ILogger? logger = null)
    {
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed)
            connection.Open();
        try
        {
            EnsureTable(connection, """
                CREATE TABLE IF NOT EXISTS Categories (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Description TEXT,
                    IsDefault INTEGER NOT NULL DEFAULT 0,
                    Type INTEGER NOT NULL DEFAULT 0,
                    Keywords TEXT,
                    Icon TEXT,
                    Color TEXT,
                    ParentCategoryId INTEGER,
                    MonthlyBudget REAL,
                    GroupName TEXT,
                    IsHidden INTEGER NOT NULL DEFAULT 0
                );
                """);
            EnsureIndex(connection, "IX_Categories_Name", "CREATE UNIQUE INDEX IF NOT EXISTS IX_Categories_Name ON Categories(Name);");

            EnsureTable(connection, """
                CREATE TABLE IF NOT EXISTS Transactions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date TEXT NOT NULL,
                    Amount REAL NOT NULL,
                    Description TEXT NOT NULL,
                    Category TEXT NOT NULL,
                    UserId TEXT NOT NULL,
                    ProjectId INTEGER,
                    PaymentMethod INTEGER,
                    Counterparty TEXT,
                    IsMandatory INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT,
                    UpdatedAt TEXT,
                    IsConfirmed INTEGER NOT NULL DEFAULT 1,
                    CounterpartyId INTEGER,
                    Notes TEXT,
                    ApprovalStatus INTEGER NOT NULL DEFAULT 1,
                    SubmittedByUserId TEXT
                );
                """);
            EnsureIndex(connection, "IX_Transactions_UserId", "CREATE INDEX IF NOT EXISTS IX_Transactions_UserId ON Transactions(UserId);");
            EnsureIndex(connection, "IX_Transactions_UserId_Date", "CREATE INDEX IF NOT EXISTS IX_Transactions_UserId_Date ON Transactions(UserId, Date);");

            AddColumnIfMissing(connection, "Transactions", "ProjectId", "INTEGER");
            AddColumnIfMissing(connection, "Transactions", "PaymentMethod", "INTEGER");
            AddColumnIfMissing(connection, "Transactions", "Counterparty", "TEXT");
            AddColumnIfMissing(connection, "Transactions", "IsMandatory", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(connection, "Transactions", "CreatedAt", "TEXT");
            AddColumnIfMissing(connection, "Transactions", "UpdatedAt", "TEXT");
            AddColumnIfMissing(connection, "Transactions", "IsConfirmed", "INTEGER NOT NULL DEFAULT 1");
            AddColumnIfMissing(connection, "Transactions", "CounterpartyId", "INTEGER");
            AddColumnIfMissing(connection, "Transactions", "Notes", "TEXT");
            AddColumnIfMissing(connection, "Transactions", "ApprovalStatus", "INTEGER NOT NULL DEFAULT 1");
            AddColumnIfMissing(connection, "Transactions", "SubmittedByUserId", "TEXT");
            AddColumnIfMissing(connection, "Transactions", "UserId", "TEXT NOT NULL DEFAULT ''");

            AddColumnIfMissing(connection, "Categories", "Keywords", "TEXT");
            AddColumnIfMissing(connection, "Categories", "Icon", "TEXT");
            AddColumnIfMissing(connection, "Categories", "Color", "TEXT");
            AddColumnIfMissing(connection, "Categories", "Type", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(connection, "Categories", "IsDefault", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(connection, "Categories", "ParentCategoryId", "INTEGER");
            AddColumnIfMissing(connection, "Categories", "MonthlyBudget", "REAL");
            AddColumnIfMissing(connection, "Categories", "GroupName", "TEXT");
            AddColumnIfMissing(connection, "Categories", "IsHidden", "INTEGER NOT NULL DEFAULT 0");

            AddColumnIfMissing(connection, "Reminders", "NotificationSentDate", "TEXT");
            AddColumnIfMissing(connection, "Reminders", "ReminderType", "INTEGER NOT NULL DEFAULT 5");
            AddColumnIfMissing(connection, "Reminders", "SnoozedUntil", "TEXT");
            AddColumnIfMissing(connection, "Reminders", "IsArchived", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(connection, "Reminders", "NotifyDaysBefore", "INTEGER NOT NULL DEFAULT 3");
        }
        finally
        {
            if (wasClosed)
                connection.Close();
        }

        logger?.LogInformation("SQLite schema repair completed.");
    }

    public static async Task RepairDataAsync(ApplicationDbContext db, ICategorizationService categorization, ILogger? logger = null)
    {
        await categorization.EnsureDefaultCategoriesAsync();

        var categories = await db.Categories.AsNoTracking().ToListAsync();
        var byName = categories
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var defaultExpense = CategoryDefaults.DefaultExpense;
        var defaultIncome = CategoryDefaults.DefaultIncome;

        if (!byName.ContainsKey(defaultExpense))
        {
            var expenseCat = new Category { Name = defaultExpense, Type = CategoryType.Expense, IsDefault = true };
            db.Categories.Add(expenseCat);
            await db.SaveChangesAsync();
            byName[defaultExpense] = expenseCat;
        }

        if (!byName.ContainsKey(defaultIncome))
        {
            var incomeCat = new Category { Name = defaultIncome, Type = CategoryType.Income, IsDefault = true };
            db.Categories.Add(incomeCat);
            await db.SaveChangesAsync();
            byName[defaultIncome] = incomeCat;
        }

        categories = await db.Categories.AsNoTracking().ToListAsync();
        byName = categories
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var txs = await db.Transactions.Where(t => t.Category != null && t.Category != "").ToListAsync();
        var fixedCategories = 0;
        var fixedAmounts = 0;

        foreach (var t in txs)
        {
            var name = t.Category.Trim();
            if (!byName.TryGetValue(name, out var cat))
            {
                var inferredType = t.Amount >= 0 ? CategoryType.Income : CategoryType.Expense;
                var newCat = new Category { Name = name, Type = inferredType, IsDefault = false };
                db.Categories.Add(newCat);
                await db.SaveChangesAsync();
                byName[name] = newCat;
                cat = newCat;
                fixedCategories++;
            }

            if (cat.Type == CategoryType.Expense && t.Amount > 0)
            {
                t.Amount = -Math.Abs(t.Amount);
                fixedAmounts++;
            }
            else if (cat.Type == CategoryType.Income && t.Amount < 0)
            {
                t.Amount = Math.Abs(t.Amount);
                fixedAmounts++;
            }

            if (!string.Equals(t.Category, cat.Name, StringComparison.Ordinal))
                t.Category = cat.Name;

            if (t.CreatedAt == null)
                t.CreatedAt = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(t.UserId))
                logger?.LogWarning("Transaction {Id} has empty UserId", t.Id);
        }

        if (fixedCategories > 0 || fixedAmounts > 0)
            await db.SaveChangesAsync();

        logger?.LogInformation("Data repair: categories fixed={Cat}, amounts sign fixed={Amt}", fixedCategories, fixedAmounts);
    }

    private static void EnsureTable(SqliteConnection connection, string createSql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = createSql;
        cmd.ExecuteNonQuery();
    }

    private static void EnsureIndex(SqliteConnection connection, string name, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string table, string column, string sqlType)
    {
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA table_info('{table}');";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                cols.Add(r.GetString(1));
        }

        if (cols.Contains(column)) return;

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {sqlType};";
        alter.ExecuteNonQuery();
    }
}
