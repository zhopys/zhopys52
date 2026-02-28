using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public interface ICsvParser
    {
        List<Transaction> Parse(Stream fileStream, string userId);
    }
}