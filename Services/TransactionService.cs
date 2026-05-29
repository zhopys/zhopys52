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

        private async Task EnsureFinanceAccessAsync(string userId)
        {
            var ctx = await _userContext.GetContextAsync(userId);
            if (!_userContext.CanManageTransactions(ctx))
                throw new UnauthorizedAccessException(AccessDeniedMessages.ForPolicy(AuthorizationPolicies.CanAccessFinances));
        }

        public async Task<Transaction> CreateAsync(Transaction transaction, string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("Пользователь не определён.", nameof(userId));

            await EnsureFinanceAccessAsync(userId);
            var ownerId = await OwnerIdAsync(userId);
            await ValidateTransactionAsync(transaction, ownerId, isNew: true);

            transaction.UserId = ownerId;
            transaction.CreatedAt = DateTime.UtcNow;
            transaction.UpdatedAt = null;
            TransactionApprovalHelper.Normalize(transaction);

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
            await EnsureFinanceAccessAsync(userId);
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
            existing.Notes = transaction.Notes;
            TransactionApprovalHelper.ApplyStatus(existing, transaction.ApprovalStatus);
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
            await EnsureFinanceAccessAsync(userId);
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
            await EnsureFinanceAccessAsync(userId);
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

        public async Task<TransactionImportResult> ImportManyAsync(IEnumerable<Transaction> transactions, string userId, ImportBatchMetadata? batchMeta = null)
        {
            await EnsureFinanceAccessAsync(userId);
            var ownerId = await OwnerIdAsync(userId);
            var result = new TransactionImportResult();
            await _categorizationService.EnsureDefaultCategoriesAsync();

            var counterpartyCache = await CounterpartyImportCache.LoadAsync(_db, ownerId);

            TransactionImportBatch? batch = null;
            if (batchMeta != null)
            {
                batch = new TransactionImportBatch
                {
                    UserId = ownerId,
                    CreatedAt = DateTime.UtcNow,
                    SourceType = batchMeta.SourceType,
                    FileName = batchMeta.FileName
                };
                _db.TransactionImportBatches.Add(batch);
                await _db.SaveChangesAsync();
                result.ImportBatchId = batch.Id;
            }

            foreach (var t in transactions)
            {
                try
                {
                    t.UserId = ownerId;
                    t.CreatedAt = DateTime.UtcNow;
                    t.UpdatedAt = null;
                    TransactionApprovalHelper.ApplyStatus(t, TransactionApprovalStatus.Approved);
                    t.ImportBatchId = batch?.Id;

                    if (string.IsNullOrWhiteSpace(t.Category))
                        t.Category = _categorizationService.CategorizeTransaction(t.Description, t.Amount);

                    if (!string.IsNullOrWhiteSpace(t.Counterparty))
                        counterpartyCache.ApplyToTransaction(_db, t);

                    await ValidateTransactionAsync(t, ownerId, isNew: true, skipCounterpartyLink: true);
                    _db.Transactions.Add(t);
                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.Errors.Add($"{t.Date:dd.MM.yyyy} {t.Description}: {ex.Message}");
                    _logger.LogWarning(ex, "Импорт: ошибка строки {Description}", t.Description);
                }
            }

            if (result.SuccessCount > 0)
            {
                try
                {
                    await _db.SaveChangesAsync();
                    if (batch != null)
                    {
                        batch.SuccessCount = result.SuccessCount;
                        batch.FailedCount = result.FailedCount;
                        await _db.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Импорт: ошибка сохранения пакета");
                    throw new InvalidOperationException("Не удалось сохранить импортированные операции.");
                }
            }
            else if (batch != null)
            {
                _db.TransactionImportBatches.Remove(batch);
                await _db.SaveChangesAsync();
                result.ImportBatchId = null;
            }

            return result;
        }

        public async Task<int> RollbackImportAsync(int batchId, string userId)
        {
            await EnsureFinanceAccessAsync(userId);
            var ownerId = await OwnerIdAsync(userId);
            var batch = await _db.TransactionImportBatches
                .FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == ownerId);
            if (batch == null)
                throw new KeyNotFoundException("Пакет импорта не найден.");
            if (batch.IsRolledBack)
                throw new InvalidOperationException("Этот импорт уже отменён.");

            var txs = await _db.Transactions
                .Where(t => t.ImportBatchId == batchId && t.UserId == ownerId)
                .ToListAsync();

            _db.Transactions.RemoveRange(txs);
            batch.IsRolledBack = true;
            batch.RolledBackAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return txs.Count;
        }

        public async Task<TransactionImportBatch?> GetImportBatchAsync(int batchId, string userId)
        {
            var ownerId = await OwnerIdAsync(userId);
            return await _db.TransactionImportBatches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == ownerId);
        }

        public async Task<TransactionImportBatch?> GetLatestImportBatchAsync(string userId)
        {
            var ownerId = await OwnerIdAsync(userId);
            return await _db.TransactionImportBatches
                .AsNoTracking()
                .Where(b => b.UserId == ownerId && !b.IsRolledBack && b.SuccessCount > 0)
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task ApproveAsync(int id, string userId)
        {
            var ctx = await _userContext.GetContextAsync(userId);
            if (!_userContext.CanApproveTransactions(ctx))
                throw new UnauthorizedAccessException("Нет прав на утверждение.");

            var ownerId = await OwnerIdAsync(userId);
            var t = await _db.Transactions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == ownerId);
            if (t == null) throw new KeyNotFoundException();
            TransactionApprovalHelper.ApplyStatus(t, TransactionApprovalStatus.Approved);
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
            TransactionApprovalHelper.ApplyStatus(t, TransactionApprovalStatus.Rejected);
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

        private async Task ValidateTransactionAsync(Transaction transaction, string userId, bool isNew, bool skipCounterpartyLink = false)
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
            if (!skipCounterpartyLink)
                await EntityLinkageHelper.ApplyCounterpartyToTransactionAsync(_db, transaction, userId);
        }

    }
}
