using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace ScreenBoxDrawer
{
    public partial class App : Application
    {
        private LowLevelMouseHook? _mouseHook;

        // DPI awareness context for per-monitor v2
        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        protected override void OnStartup(StartupEventArgs e)
        {
            // Try to set per-monitor DPI awareness so WPF can work correctly across monitors.
            try
            {
                SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetProcessDpiAwarenessContext failed: {ex}");
            }

            base.OnStartup(e);
            Debug.WriteLine("ScreenBoxDrawer: OnStartup called");
            var main = new MainWindow();
            MainWindow = main;
            main.Show();
            _mouseHook = new LowLevelMouseHook(main);
            _mouseHook.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mouseHook?.Stop();
            base.OnExit(e);
        }
    }
}
