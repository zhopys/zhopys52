namespace MiniFinance.Services;

public static class JsDialogHelper
{
    public static Task<bool> ConfirmAsync(ConfirmDialogService confirm, string message, string? title = null, bool isDanger = true) =>
        confirm.ShowAsync(new ConfirmDialogOptions
        {
            Title = title ?? "Подтверждение",
            Message = message,
            ConfirmText = isDanger ? "Удалить" : "Подтвердить",
            IsDanger = isDanger
        });

    public static Task<bool> ConfirmDeleteAsync(ConfirmDialogService confirm, string itemLabel, string? detail = null) =>
        confirm.ConfirmDeleteAsync(itemLabel, detail);
}
