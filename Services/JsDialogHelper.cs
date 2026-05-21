using Microsoft.JSInterop;

namespace MiniFinance.Services;

public static class JsDialogHelper
{
    public static ValueTask<bool> ConfirmAsync(IJSRuntime js, string message) =>
        js.InvokeAsync<bool>("confirm", message);
}
