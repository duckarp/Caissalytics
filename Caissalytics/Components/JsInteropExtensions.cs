using Microsoft.JSInterop;

namespace Caissalytics.Components;

public static class JsInteropExtensions
{
    /// <summary>
    /// Calls the JS object's own dispose() (removing its listeners) and then releases the .NET-side
    /// reference, without which the JS runtime keeps the object and its DOM element alive.
    /// Errors are ignored: this runs during teardown, possibly after the page is gone.
    /// </summary>
    public static async Task DisposeJsInstanceAsync(this IJSObjectReference? instance)
    {
        if (instance == null) return;
        try
        {
            await instance.InvokeVoidAsync("dispose");
        }
        catch { }
        try
        {
            await instance.DisposeAsync();
        }
        catch { }
    }
}
