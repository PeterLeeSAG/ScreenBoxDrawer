using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using Forms = System.Windows.Forms;

namespace ScreenBoxDrawer
{
    public partial class App : System.Windows.Application
    {
        private LowLevelMouseHook? _mouseHook;
        private MainWindow? _overlay;
        private Forms.NotifyIcon? _notifyIcon;
        private Forms.ContextMenuStrip? _trayMenu;
        private AppSettings _settings = new();

        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetProcessDpiAwarenessContext failed: {ex}");
            }

            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                _settings = SettingsStore.Load();
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is JsonException)
            {
                System.Windows.MessageBox.Show(
                    $"Could not load saved settings. Default settings will be used.\n\n{ex.Message}",
                    "ScreenBoxDrawer",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                _settings = new AppSettings();
            }

            _overlay = new MainWindow(_settings);
            MainWindow = _overlay;
            _overlay.ClearRequested += (_, _) => _mouseHook?.CancelDrawing();
            _overlay.Show();

            _mouseHook = new LowLevelMouseHook(_overlay);
            _mouseHook.Start();

            CreateTrayIcon();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mouseHook?.Stop();
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            _trayMenu?.Dispose();
            base.OnExit(e);
        }

        private void CreateTrayIcon()
        {
            _trayMenu = new Forms.ContextMenuStrip();
            _trayMenu.Items.Add("Settings...", null, (_, _) => OpenSettings());
            _trayMenu.Items.Add("Clear all boxes", null, (_, _) =>
                Dispatcher.BeginInvoke(new Action(() => _overlay?.ClearBoxes())));
            _trayMenu.Items.Add(new Forms.ToolStripSeparator());
            _trayMenu.Items.Add("Exit", null, (_, _) =>
                Dispatcher.BeginInvoke(new Action(Shutdown)));

            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "ScreenBoxDrawer",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _notifyIcon.DoubleClick += (_, _) => OpenSettings();
        }

        private void OpenSettings()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_overlay == null)
                    return;

                var window = new SettingsWindow(_settings, _overlay);
                window.ShowDialog();
                if (window.SavedSettings != null)
                    _settings = window.SavedSettings;
            }));
        }
    }
}
