namespace MiniFinance.Services
{
    public interface ICategorizationService
    {
        string CategorizeTransaction(string description, decimal amount);
    }
}
