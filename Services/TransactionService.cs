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
        private readonly IDataScopeService _dataScope;

        public TransactionService(ApplicationDbContext db, ILogger<TransactionService> logger,
            ICategorizationService categorizationService, IUserContextService userContext, IDataScopeService dataScope)
        {
            _db = db;
            _logger = logger;
            _categorizationService = categorizationService;
            _userContext = userContext;
            _dataScope = dataScope;
        }

        private Task<string> OwnerIdAsync(string userId) => _dataScope.GetDataOwnerUserIdAsync(userId);

        public async Task<Transaction> CreateAsync(Transaction transaction, string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("Пользователь не определён.", nameof(userId));

            var ownerId = await OwnerIdAsync(userId);
            await ValidateTransactionAsync(transaction, ownerId, isNew: true);

            transaction.UserId = ownerId;
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
            var ownerId = await OwnerIdAsync(userId);
            var existing = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == transaction.Id && t.UserId == ownerId);
            if (existing == null)
                throw new KeyNotFoundException("Транзакция не найдена или доступ запрещён.");

            existing.Date = transaction.Date.Date;
            existing.Amount = transaction.Amount;
            existing.Description = transaction.Description.Trim();
            existing.Category = transaction.Category;
            existing.ProjectId = transaction.ProjectId;
            existing.PaymentMethod = transaction.PaymentMethod;
            existing.IsMandatory = transaction.IsMandatory;
            existing.IsConfirmed = transaction.IsConfirmed;
            existing.Notes = transaction.Notes;
            existing.CounterpartyId = transaction.CounterpartyId;
            existing.Counterparty = transaction.Counterparty;

            await ValidateTransactionAsync(existing, ownerId, isNew: false);
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

            var ownerId = await OwnerIdAsync(userId);
            var existing = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == ownerId);
            if (existing == null)
                throw new KeyNotFoundException("Транзакция не найдена или доступ запрещён.");

            var cat = await _categorizationService.EnsureCategoryAsync(category.Trim(), existing.Amount);
            existing.Category = cat.Name;
            if (cat.Type == CategoryType.Expense && existing.Amount > 0)
                existing.Amount = -Math.Abs(existing.Amount);
            else if (cat.Type == CategoryType.Income && existing.Amount < 0)
                existing.Amount = Math.Abs(existing.Amount);

            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return existing;
        }

        public async Task DeleteAsync(int id, string userId)
        {
            var ownerId = await OwnerIdAsync(userId);
            var t = await _db.Transactions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == ownerId);
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

        public async Task<Transaction?> GetAsync(int id, string userId)
        {
            var ownerId = await OwnerIdAsync(userId);
            var ctx = await _userContext.GetContextAsync(userId);
            var q = _db.Transactions
                .Include(x => x.Project)
                .Include(x => x.CounterpartyEntity)
                .Where(x => x.Id == id && x.UserId == ownerId);
            q = _userContext.FilterTransactionsForRole(q, ctx);
            return await q.FirstOrDefaultAsync();
        }

        public async Task<List<Transaction>> ListAsync(string userId, TransactionListFilter? filter = null)
        {
            var ownerId = await OwnerIdAsync(userId);
            var ctx = await _userContext.GetContextAsync(userId);
            var q = _db.Transactions.Include(t => t.Project).AsQueryable().Where(t => t.UserId == ownerId);
            q = _userContext.FilterTransactionsForRole(q, ctx);

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

            var ownerId = await OwnerIdAsync(userId);
            var t = await _db.Transactions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == ownerId);
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

            var ownerId = await OwnerIdAsync(userId);
            var t = await _db.Transactions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == ownerId);
            if (t == null) throw new KeyNotFoundException();
            t.ApprovalStatus = TransactionApprovalStatus.Rejected;
            t.IsConfirmed = false;
            t.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        public async Task<List<Transaction>> ListPendingApprovalAsync(string userId)
        {
            var ownerId = await OwnerIdAsync(userId);
            return await _db.Transactions
                .Where(t => t.UserId == ownerId && t.ApprovalStatus == TransactionApprovalStatus.Pending)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<HashSet<string>> GetExistingHashesAsync(string userId)
        {
            var ownerId = await OwnerIdAsync(userId);
            var rows = await _db.Transactions
                .Where(t => t.UserId == ownerId)
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

            var cat = await _categorizationService.EnsureCategoryAsync(categoryName, transaction.Amount);

            if (cat.Type == CategoryType.Expense && transaction.Amount > 0)
                transaction.Amount = -Math.Abs(transaction.Amount);
            else if (cat.Type == CategoryType.Income && transaction.Amount < 0)
                transaction.Amount = Math.Abs(transaction.Amount);

            transaction.Category = cat.Name;

            await EntityLinkageHelper.ValidateProjectAsync(_db, transaction.ProjectId, userId);
            await EntityLinkageHelper.ApplyCounterpartyToTransactionAsync(_db, transaction, userId);
        }

    }
}
