using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public class TransactionService : ITransactionService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<TransactionService> _logger;
        private readonly ICategorizationService _categorizationService;
        private readonly IUserContextService _userContext;

        public TransactionService(ApplicationDbContext db, ILogger<TransactionService> logger,
            ICategorizationService categorizationService, IUserContextService userContext)
        {
            _db = db;
            _logger = logger;
            _categorizationService = categorizationService;
            _userContext = userContext;
        }

        public async Task<Transaction> CreateAsync(Transaction transaction, string userId)
        {
            await ValidateTransactionAsync(transaction, userId, isNew: true);

            transaction.UserId = userId;
            transaction.CreatedAt = DateTime.UtcNow;
            transaction.UpdatedAt = null;

            var ctx = await _userContext.GetContextAsync(userId);
            if (ctx.IsManager && !ctx.IsOwner)
            {
                transaction.ApprovalStatus = TransactionApprovalStatus.Pending;
                transaction.IsConfirmed = false;
                transaction.SubmittedByUserId = userId;
            }
            else
            {
                transaction.ApprovalStatus = TransactionApprovalStatus.Approved;
                transaction.IsConfirmed = true;
            }

            if (string.IsNullOrWhiteSpace(transaction.Category))
                transaction.Category = _categorizationService.CategorizeTransaction(transaction.Description, transaction.Amount);

            _db.Transactions.Add(transaction);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании транзакции");
                throw new InvalidOperationException("Не удалось сохранить транзакцию. Проверьте данные и попробуйте снова.");
            }

            return transaction;
        }

        public async Task<Transaction> UpdateAsync(Transaction transaction, string userId)
        {
            await ValidateTransactionAsync(transaction, userId, isNew: false);

            var existing = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == transaction.Id && t.UserId == userId);
            if (existing == null)
                throw new KeyNotFoundException("Транзакция не найдена или доступ запрещён.");

            existing.Date = transaction.Date.Date;
            existing.Amount = transaction.Amount;
            existing.Description = transaction.Description.Trim();
            existing.Category = string.IsNullOrWhiteSpace(transaction.Category)
                ? _categorizationService.CategorizeTransaction(transaction.Description, transaction.Amount)
                : transaction.Category.Trim();
            existing.Counterparty = transaction.Counterparty;
            existing.ProjectId = transaction.ProjectId;
            existing.PaymentMethod = transaction.PaymentMethod;
            existing.IsMandatory = transaction.IsMandatory;
            existing.IsConfirmed = transaction.IsConfirmed;
            existing.Notes = transaction.Notes;
            existing.CounterpartyId = transaction.CounterpartyId;
            if (!string.IsNullOrWhiteSpace(transaction.Counterparty))
                existing.Counterparty = transaction.Counterparty.Trim();
            existing.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении транзакции {Id}", transaction.Id);
                throw new InvalidOperationException("Не удалось обновить транзакцию. Проверьте данные и попробуйте снова.");
            }

            return existing;
        }

        public async Task<Transaction> UpdateCategoryAsync(int id, string category, string userId)
        {
            if (string.IsNullOrWhiteSpace(category))
                throw new ArgumentException("Категория не может быть пустой.", nameof(category));

            var existing = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
            if (existing == null)
                throw new KeyNotFoundException("Транзакция не найдена или доступ запрещён.");

            existing.Category = category.Trim();
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return existing;
        }

        public async Task DeleteAsync(int id, string userId)
        {
            var t = await _db.Transactions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
            if (t == null)
                throw new KeyNotFoundException("Транзакция не найдена или доступ запрещён.");

            _db.Transactions.Remove(t);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении транзакции {Id}", id);
                throw new InvalidOperationException("Не удалось удалить транзакцию. Возможно, она используется в отчётах.");
            }
        }

        public async Task<Transaction?> GetAsync(int id, string userId) =>
            await _db.Transactions.Include(x => x.Project)
                .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);

        public async Task<List<Transaction>> ListAsync(string userId, TransactionListFilter? filter = null)
        {
            var q = _db.Transactions.AsQueryable().Where(t => t.UserId == userId);

            if (filter != null)
            {
                if (filter.From.HasValue)
                    q = q.Where(t => t.Date >= filter.From.Value);
                if (filter.To.HasValue)
                    q = q.Where(t => t.Date <= filter.To.Value);
                if (filter.ProjectId.HasValue)
                    q = q.Where(t => t.ProjectId == filter.ProjectId.Value);
                if (!string.IsNullOrWhiteSpace(filter.Category))
                    q = q.Where(t => t.Category == filter.Category);
                if (!string.IsNullOrWhiteSpace(filter.Search))
                {
                    var s = filter.Search.Trim();
                    q = q.Where(t => t.Description.Contains(s) || t.Category.Contains(s) ||
                                     (t.Counterparty != null && t.Counterparty.Contains(s)));
                }
                if (filter.Type == "income")
                    q = q.Where(t => t.Amount > 0);
                else if (filter.Type == "expense")
                    q = q.Where(t => t.Amount < 0);
            }

            return await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).ToListAsync();
        }

        public async Task<TransactionImportResult> ImportManyAsync(IEnumerable<Transaction> transactions, string userId)
        {
            var result = new TransactionImportResult();
            await _categorizationService.EnsureDefaultCategoriesAsync();

            foreach (var t in transactions)
            {
                try
                {
                    await CreateAsync(t, userId);
                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.Errors.Add($"{t.Date:dd.MM.yyyy} {t.Description}: {ex.Message}");
                    _logger.LogWarning(ex, "Импорт: ошибка строки {Description}", t.Description);
                }
            }

            return result;
        }

        public async Task ApproveAsync(int id, string userId)
        {
            var ctx = await _userContext.GetContextAsync(userId);
            if (!_userContext.CanApproveTransactions(ctx))
                throw new UnauthorizedAccessException("Нет прав на утверждение.");

            var t = await _db.Transactions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
            if (t == null) throw new KeyNotFoundException();
            t.ApprovalStatus = TransactionApprovalStatus.Approved;
            t.IsConfirmed = true;
            t.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        public async Task RejectAsync(int id, string userId)
        {
            var ctx = await _userContext.GetContextAsync(userId);
            if (!_userContext.CanApproveTransactions(ctx))
                throw new UnauthorizedAccessException("Нет прав на отклонение.");

            var t = await _db.Transactions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
            if (t == null) throw new KeyNotFoundException();
            t.ApprovalStatus = TransactionApprovalStatus.Rejected;
            t.IsConfirmed = false;
            t.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        public Task<List<Transaction>> ListPendingApprovalAsync(string userId) =>
            _db.Transactions
                .Where(t => t.UserId == userId && t.ApprovalStatus == TransactionApprovalStatus.Pending)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

        public async Task<HashSet<string>> GetExistingHashesAsync(string userId)
        {
            var rows = await _db.Transactions
                .Where(t => t.UserId == userId)
                .Select(t => new { t.Date, t.Amount, t.Description })
                .ToListAsync();

            return rows
                .Select(t => TransactionHash.Compute(t.Date, t.Amount, t.Description))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private async Task ValidateTransactionAsync(Transaction transaction, string userId, bool isNew)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));

            if (transaction.Date.Date > DateTime.Today.AddDays(1))
                throw new TransactionValidationException("Дата не может быть позже текущей даты + 1 день.");

            if (transaction.Amount == 0)
                throw new TransactionValidationException("Сумма не может быть равна нулю.");

            if (string.IsNullOrWhiteSpace(transaction.Description))
                throw new TransactionValidationException("Описание обязательно.");

            transaction.Description = transaction.Description.Trim();

            await _categorizationService.EnsureDefaultCategoriesAsync();

            var categoryName = string.IsNullOrWhiteSpace(transaction.Category)
                ? _categorizationService.CategorizeTransaction(transaction.Description, transaction.Amount)
                : transaction.Category.Trim();

            var cat = await _db.Categories.FirstOrDefaultAsync(c => c.Name == categoryName);
            if (cat == null)
                throw new TransactionValidationException($"Категория «{categoryName}» не найдена. Создайте её на странице «Категории».");

            if (cat.Type == CategoryType.Expense && transaction.Amount > 0)
                throw new TransactionValidationException("Для категории расхода сумма должна быть отрицательной.");

            if (cat.Type == CategoryType.Income && transaction.Amount < 0)
                throw new TransactionValidationException("Для категории дохода сумма должна быть положительной.");

            transaction.Category = categoryName;

            if (transaction.ProjectId.HasValue)
            {
                var proj = await _db.Projects.FirstOrDefaultAsync(p => p.Id == transaction.ProjectId.Value);
                if (proj == null)
                    throw new TransactionValidationException("Указанный проект не найден.");

                if (!proj.IsDefault && proj.UserId != userId)
                    throw new TransactionValidationException("Проект не принадлежит текущему пользователю.");
            }
        }

    }
}
