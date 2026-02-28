using Microsoft.AspNetCore.Identity;

namespace MiniFinance.Data
{
    // Добавьте свойства пользователя, если нужно
    public class ApplicationUser : IdentityUser
    {
        // Навигационное свойство для связи с транзакциями
        public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    }
}