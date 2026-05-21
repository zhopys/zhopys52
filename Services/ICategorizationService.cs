using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public interface ICategorizationService
    {
        string CategorizeTransaction(string description, decimal amount);
        Task EnsureDefaultCategoriesAsync();
        Task<Category> EnsureCategoryAsync(string name, decimal amount);
        Task<List<string>> GetCategoryNamesAsync();
        Task<Category?> GetCategoryAsync(int id);
        Task<CategoryStatsDto> GetCategoryStatsAsync(int categoryId, string userId);
        Task<Category> UpdateCategoryAsync(CategoryUpdateRequest request);
        Task SetCategoryHiddenAsync(int id, bool hidden);
    }

    public sealed class CategoryUpdateRequest
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public CategoryType Type { get; init; }
        public string? Keywords { get; init; }
        public string? Icon { get; init; }
        public string? Color { get; init; }
        public string? Description { get; init; }
        public decimal? MonthlyBudget { get; init; }
    }
}
