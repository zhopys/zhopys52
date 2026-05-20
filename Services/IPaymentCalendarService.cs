namespace MiniFinance.Services;

public interface IPaymentCalendarService
{
    Task<PaymentCalendarDto> BuildMonthAsync(string userId, int year, int month);
    Task<PaymentCalendarItemDto> AddPaymentAsync(PaymentCreateRequest request, string userId);
    Task<PaymentBulkPayResult> MarkPaidBulkAsync(IEnumerable<string> itemKeys, string userId);
    Task MarkPaidAsync(string itemKey, string userId);
}
