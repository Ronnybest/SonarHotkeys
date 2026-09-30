using System.Runtime.InteropServices;

namespace SonarHotkeys
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int processId);

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            using var instance = new Mutex(true, @"Local\SonarHotkeys.SingleInstance", out bool firstInstance);
            using var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SonarHotkeys.ShowWindow");
            if (!firstInstance)
            {
                // Bring the running copy to the front instead of exiting silently.
                // The launched process owns the foreground right and passes it on (ASFW_ANY).
                AllowSetForegroundWindow(-1);
                showRequest.Set();
                return;
            }

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            using var form = new Form1();
            var wait = ThreadPool.RegisterWaitForSingleObject(showRequest, (_, _) =>
            {
                // A request before the handle exists or after closing is dropped: the window is shown or exiting anyway.
                try { form.BeginInvoke(form.ShowWindow); }
                catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { }
            }, null, Timeout.Infinite, executeOnlyOnce: false);
            Application.Run(form);
            wait.Unregister(null);
            instance.ReleaseMutex();
        }
    }
}
