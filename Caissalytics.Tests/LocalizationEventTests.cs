using Caissalytics.Data;
using Caissalytics.Localization;
using Microsoft.JSInterop;
using Xunit;

namespace Caissalytics.Tests;

public class LocalizationEventTests
{
    [Fact]
    public async Task FailingSubscriber_DoesNotStopOthersFromBeingNotified()
    {
        var loc = new LocalizationService(loadPersistedSettings: false);
        bool secondNotified = false;
        loc.OnLanguageChanged += () => throw new InvalidOperationException("component already disposed");
        loc.OnLanguageChanged += () => secondNotified = true;

        await loc.SetLanguageAsync("sk");

        Assert.True(secondNotified);
    }

    [Fact]
    public async Task LanguageLoadedLater_IsAnnouncedOnTheCreatingSynchronizationContext()
    {
        var settings = new TaskCompletionSource<AppearanceSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        var appearance = new PendingAppearanceService(settings.Task);
        var uiContext = new RecordingSynchronizationContext();

        LocalizationService loc;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(uiContext);
        try
        {
            loc = new LocalizationService(appearance, loadPersistedSettings: true);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var raisedOn = new TaskCompletionSource<SynchronizationContext?>(TaskCreationOptions.RunContinuationsAsynchronously);
        loc.OnLanguageChanged += () => raisedOn.TrySetResult(SynchronizationContext.Current);

        string other = loc.CurrentLanguage == "sk" ? "en" : "sk";
        settings.SetResult(new AppearanceSettings { Language = other });

        Assert.Same(uiContext, await raisedOn.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(other, loc.CurrentLanguage);
    }

    /// <summary>Runs posted work on the thread pool with itself installed as the current context.</summary>
    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                SetSynchronizationContext(this);
                d(state);
            });
        }
    }

    private sealed class PendingAppearanceService(Task<AppearanceSettings> settings) : IAppearanceService
    {
        public Task<AppearanceSettings> GetSettingsAsync() => settings;
        public Task SaveSettingsAsync(AppearanceSettings s) => Task.CompletedTask;
        public Task PlaySoundAsync(ChessSoundType soundType, IJSRuntime? jsRuntime = null) => Task.CompletedTask;
        public void SetJSRuntime(IJSRuntime jsRuntime) { }
        public event Action<AppearanceSettings>? OnAppearanceChanged { add { } remove { } }
    }
}
