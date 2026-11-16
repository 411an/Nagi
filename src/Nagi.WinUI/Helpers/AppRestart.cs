using Nagi.Core.Helpers;
using Nagi.Core.Services.Abstractions;
using Microsoft.Windows.AppLifecycle;

namespace Nagi.WinUI.Helpers;

/// <summary>
///     Restarts the unpackaged process while preserving <c>--datadir</c> when it came from argv.
/// </summary>
internal static class AppRestart
{
    public static async Task FlushAndRestartAsync(ISettingsService? settings, params string[] extraFlags)
    {
        if (settings is not null)
            await settings.FlushAsync();

        AppInstance.Restart(AppDataLocator.BuildRestartArguments(extraFlags));
    }

    public static void Restart(params string[] extraFlags)
    {
        AppInstance.Restart(AppDataLocator.BuildRestartArguments(extraFlags));
    }
}
