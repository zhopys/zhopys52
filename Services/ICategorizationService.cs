using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public interface ICategorizationService
    {
        string CategorizeTransaction(string description, decimal amount);
        Task EnsureDefaultCategoriesAsync();
        Task<Category> EnsureCategoryAsync(string name, decimal amount);
        Task<List<string>> GetCategoryNamesAsync();
        Task<Category> UpdateCategoryAsync(int id, string name, CategoryType type, string? keywords);
    }
}
