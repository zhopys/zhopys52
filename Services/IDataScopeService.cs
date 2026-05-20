namespace MiniFinance.Services;

/// <summary>
/// Определяет владельца данных организации (общее рабочее пространство для команды).
/// </summary>
public interface IDataScopeService
{
    Task<string> GetDataOwnerUserIdAsync(string currentUserId);
}
