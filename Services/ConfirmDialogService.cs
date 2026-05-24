namespace MiniFinance.Services;

public sealed class ConfirmDialogOptions
{
    public string Title { get; init; } = "Подтверждение";
    public string Message { get; init; } = "";
    public string? Detail { get; init; }
    public string ConfirmText { get; init; } = "Подтвердить";
    public string CancelText { get; init; } = "Отмена";
    public bool IsDanger { get; init; }
}

public sealed class ConfirmDialogService
{
    public event Action? StateChanged;

    public bool IsVisible { get; private set; }
    public ConfirmDialogOptions? Options { get; private set; }

    private TaskCompletionSource<bool>? _tcs;

    public Task<bool> ShowAsync(ConfirmDialogOptions options)
    {
        Options = options;
        IsVisible = true;
        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Notify();
        return _tcs.Task;
    }

    public Task<bool> ConfirmDeleteAsync(string itemLabel, string? detail = null) =>
        ShowAsync(new ConfirmDialogOptions
        {
            Title = "Удалить запись?",
            Message = $"Будет удалено: {itemLabel}.",
            Detail = detail,
            ConfirmText = "Удалить",
            CancelText = "Отмена",
            IsDanger = true
        });

    public void Accept()
    {
        IsVisible = false;
        Options = null;
        _tcs?.TrySetResult(true);
        _tcs = null;
        Notify();
    }

    public void Dismiss()
    {
        IsVisible = false;
        Options = null;
        _tcs?.TrySetResult(false);
        _tcs = null;
        Notify();
    }

    private void Notify() => StateChanged?.Invoke();
}
