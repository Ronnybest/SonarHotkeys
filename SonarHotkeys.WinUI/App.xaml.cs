using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace SonarHotkeys;

public partial class App : Application
{
    private readonly EventWaitHandle _showRequest;
    private TrayController? _controller;
    private RegisteredWaitHandle? _showWait;

    public App(EventWaitHandle showRequest)
    {
        _showRequest = showRequest;
        InitializeComponent();
    }

    private static bool StartHidden =>
#if DEBUG
        false; // Debug runs always show the window.
#else
        Environment.GetCommandLineArgs().Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
#endif

    // Debug runs can point at a scratch settings file, so trying the UI never touches the real one.
    private static string SettingsPath =>
#if DEBUG
        Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--settings=", StringComparison.OrdinalIgnoreCase))?["--settings=".Length..]
            ?? AppSettings.FilePath;
#else
        AppSettings.FilePath;
#endif

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _controller = new TrayController(SettingsPath);
        _controller.Exited += () =>
        {
            _showWait?.Unregister(null);
            Exit();
        };
        // Another launch asks this copy to show its window.
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showRequest,
            (_, _) => dispatcher.TryEnqueue(() => _controller.ShowWindow()), null, Timeout.Infinite, executeOnlyOnce: false);
        _controller.Start(StartHidden);
    }
}
