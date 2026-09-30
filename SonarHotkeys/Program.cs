namespace SonarHotkeys
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            using var instance = new Mutex(true, @"Local\SonarHotkeys.SingleInstance", out bool firstInstance);
            if (!firstInstance)
                return;

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
            instance.ReleaseMutex();
        }
    }
}
