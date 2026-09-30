using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SonarHotkeys.Interop;

namespace SonarHotkeys;

public static class Program
{
    // Shared with earlier versions, so an old and a new copy never run side by side.
    private const string InstanceName = @"Local\SonarHotkeys.SingleInstance";
    private const string ShowRequestName = @"Local\SonarHotkeys.ShowWindow";

    [STAThread]
    private static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        using var instance = new Mutex(true, InstanceName, out bool firstInstance);
        using var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, ShowRequestName);
        if (!firstInstance)
        {
            // Bring the running copy to the front instead of exiting silently.
            // The launched process owns the foreground right and passes it on.
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            showRequest.Set();
            return 0;
        }
        Application.Start(_ =>
        {
            var dispatcher = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(dispatcher));
            new App(showRequest);
        });
        instance.ReleaseMutex();
        return 0;
    }
}
