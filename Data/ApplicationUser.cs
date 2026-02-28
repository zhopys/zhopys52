using Microsoft.AspNetCore.Identity;
using MiniFinance.Data.Models;

namespace MiniFinance.Data
{
    // Добавьте свойства пользователя, если нужно
    public class ApplicationUser : IdentityUser
    {
        // Навигационное свойство для связи с транзакциями
        public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    }
}