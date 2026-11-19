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
        private System.Drawing.Icon? _trayIcon;
        private CaptureWindow? _captureWindow;
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
            _overlay.CaptureRequested += (_, _) => ToggleCaptureMode();
            _overlay.Show();

            _mouseHook = new LowLevelMouseHook(_overlay);
            if (!_mouseHook.Start(out var hookError))
            {
                System.Windows.MessageBox.Show(
                    $"The mouse input hook could not be installed. Drawing will not work.\n\n{hookError}",
                    "ScreenBoxDrawer",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }

            CreateTrayIcon();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _captureWindow?.Close();
            _mouseHook?.Stop();
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            _trayIcon?.Dispose();
            _trayMenu?.Dispose();
            base.OnExit(e);
        }

        private void CreateTrayIcon()
        {
            _trayMenu = new Forms.ContextMenuStrip();
            _trayMenu.Items.Add("Settings...", null, (_, _) => OpenSettings());
            _trayMenu.Items.Add("Capture screen", null, (_, _) => ToggleCaptureMode());
            _trayMenu.Items.Add("Clear all boxes", null, (_, _) =>
                Dispatcher.BeginInvoke(new Action(() => _overlay?.ClearBoxes())));
            _trayMenu.Items.Add(new Forms.ToolStripSeparator());
            _trayMenu.Items.Add("Exit", null, (_, _) =>
                Dispatcher.BeginInvoke(new Action(Shutdown)));

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "screenboxdrawer.ico");
            _trayIcon = new System.Drawing.Icon(iconPath);
            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = _trayIcon,
                Text = "ScreenBoxDrawer",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _notifyIcon.DoubleClick += (_, _) => OpenSettings();
        }

        private void ToggleCaptureMode()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_captureWindow != null)
                {
                    _captureWindow.Close();
                    return;
                }

                if (_overlay == null)
                    return;

                _overlay.SetCaptureModeActive(true);
                try
                {
                    var captureWindow = new CaptureWindow(_settings);
                    captureWindow.Closed += CaptureWindow_Closed;
                    _captureWindow = captureWindow;
                    captureWindow.Show();
                }
                catch (Exception ex) when (
                    ex is ArgumentException ||
                    ex is ExternalException ||
                    ex is InvalidOperationException)
                {
                    _overlay.SetCaptureModeActive(false);
                    System.Windows.MessageBox.Show(
                        $"The screen could not be captured.\n\n{ex.Message}",
                        "ScreenBoxDrawer",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }));
        }

        private void CaptureWindow_Closed(object? sender, EventArgs e)
        {
            if (ReferenceEquals(sender, _captureWindow))
                _captureWindow = null;

            _overlay?.SetCaptureModeActive(false);
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
