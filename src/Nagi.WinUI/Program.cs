using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Settings;
using Nagi.Core.Helpers;
using Nagi.WinUI.Helpers;
using WinRT;

namespace Nagi.WinUI;

/// <summary>
///     Application entry point with single-instance enforcement using Mutex and Named Pipes.
/// </summary>
public static class Program
{
    private static SingleInstanceManager? _singleInstanceManager;

    [STAThread]
    private static int Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();

#if !DEBUG
        Thread.Sleep(100);
#endif

        try
        {
            AppDataLocator.Resolve(AppContext.BaseDirectory, args);
        }
        catch (AppDataLocatorException ex)
        {
            NativeMessageBox(ex.Message);
            return 1;
        }

        var logger = CreateBootstrapLogger();
        _singleInstanceManager = new SingleInstanceManager(logger);

        if (!_singleInstanceManager.TryAcquire())
        {
            logger?.LogInformation("Secondary instance detected, sending activation to primary");
            var filePath = CommandLineArgs.TryGetFilePath(args);

            using var activationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var activationSent = _singleInstanceManager
                .SendActivationAsync(filePath, activationTimeout.Token)
                .GetAwaiter()
                .GetResult();

            if (activationSent)
            {
                logger?.LogInformation("Activation message sent successfully, exiting");
            }
            else
            {
                logger?.LogWarning("Failed to send activation message, exiting anyway");
            }

            _singleInstanceManager.Dispose();
            return 0;
        }

        _singleInstanceManager.ActivationReceived += OnActivationReceived;

        logger?.LogInformation("Primary instance starting");

        LanguageBootstrapper.Bootstrap();

        XamlOptionalChanges.EnableChange(XamlChangeId.DefaultStyleOptimizations);
        XamlOptionalChanges.EnableChange(XamlChangeId.DeferContextFlyoutInit);
        XamlOptionalChanges.EnableChange(XamlChangeId.IconNoGridOptimization);
        XamlOptionalChanges.EnableChange(XamlChangeId.OptimizeApplyStyles);

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        _singleInstanceManager.ActivationReceived -= OnActivationReceived;
        _singleInstanceManager.Dispose();

        return 0;
    }

    private static ILogger<SingleInstanceManager>? CreateBootstrapLogger() => null;

    private static void OnActivationReceived(string? filePath)
    {
        App.CurrentApp?.HandleExternalActivation(filePath);
    }

    public static string? TryGetFilePathFromArgs(string[] args) => CommandLineArgs.TryGetFilePath(args);

    private static void NativeMessageBox(string message)
    {
        MessageBox(IntPtr.Zero, message, "Nagi", 0x00000010);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);
}
