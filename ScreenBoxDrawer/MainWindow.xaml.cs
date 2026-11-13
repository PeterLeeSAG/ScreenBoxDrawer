using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using MediaColor = System.Windows.Media.Color;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace ScreenBoxDrawer
{
    public partial class MainWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_FRAMECHANGED = 0x0020;
        private const int WM_HOTKEY = 0x0312;
        private const int DrawHotkeyId = 1;
        private const int ClearHotkeyId = 2;

        private readonly Random _random;
        private readonly List<DrawBox> _boxes = new();
        private AppSettings _settings;
        private HwndSource? _windowSource;
        private IntPtr _windowHandle;
        private System.Windows.Point _start;
        private DrawBox? _activeBox;
        private bool _isDrawing;
        private bool _drawArmed;
        private bool _hotkeysRegistered;

        public event EventHandler? ClearRequested;

        public MainWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings.Clone();
            _random = Config.CreateRandom();
        }

        public bool IsDrawArmed => _drawArmed;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            _windowHandle = new WindowInteropHelper(this).EnsureHandle();
            var extendedStyle = NativeMethods.GetWindowLong(_windowHandle, GWL_EXSTYLE);
            extendedStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLong(_windowHandle, GWL_EXSTYLE, extendedStyle);
            MakeWindowClickThrough();

            _windowSource = HwndSource.FromHwnd(_windowHandle)
                ?? throw new InvalidOperationException("The overlay window message source could not be created.");
            _windowSource.AddHook(WindowProc);

            if (!TryApplySettings(_settings, out var error))
            {
                System.Windows.MessageBox.Show(
                    $"A global shortcut could not be registered. {error}\n\n" +
                    "The existing Ctrl+Shift+drag gesture is still available.",
                    "ScreenBoxDrawer",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        public bool TryApplySettings(AppSettings settings, out string error)
        {
            if (!Dispatcher.CheckAccess())
            {
                var result = Dispatcher.Invoke(() => TryApplySettings(settings, out var innerError)
                    ? (Success: true, Error: string.Empty)
                    : (Success: false, Error: innerError));
                error = result.Error;
                return result.Success;
            }

            var validationError = settings.Validate();
            if (validationError != null)
            {
                error = validationError;
                return false;
            }

            if (!TryRegisterHotkeys(settings, out error))
                return false;

            _settings = settings.Clone();
            Config.MaximumVisibleBoxes = _settings.MaximumVisibleBoxes;
            Config.BoxDisplaySeconds = _settings.BoxDisplaySeconds;
            Config.EffectsEnabled = _settings.EffectsEnabled;

            while (_boxes.Count > Config.MaximumVisibleBoxes)
                RemoveBox(_boxes[0]);

            error = string.Empty;
            return true;
        }

        public void ArmDraw()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(ArmDraw);
                return;
            }

            if (!_isDrawing)
                _drawArmed = true;
        }

        public void ClearBoxes()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(ClearBoxes);
                return;
            }

            ClearRequested?.Invoke(this, EventArgs.Empty);
            _drawArmed = false;
            _isDrawing = false;
            _activeBox = null;

            foreach (var box in _boxes.ToArray())
                RemoveBox(box);

            NativeMethods.ReleaseCapture();
            ToggleOverlayClickThrough(true);
            Visibility = Visibility.Hidden;
        }

        public bool BeginDraw(System.Drawing.Point startScreen)
        {
            return Dispatcher.Invoke(() => BeginDrawCore(startScreen));
        }

        private bool BeginDrawCore(System.Drawing.Point startScreen)
        {
            if (_isDrawing)
                return false;

            while (_boxes.Count >= Config.MaximumVisibleBoxes)
                RemoveBox(_boxes[0]);

            ToggleOverlayClickThrough(false);
            NativeMethods.SetCapture(_windowHandle);
            _drawArmed = false;
            _start = ConvertScreenPixelsToWindowDips(startScreen);

            var color = RandomColor();
            var rectangle = new WpfRectangle
            {
                Stroke = new SolidColorBrush(color),
                Fill = Config.UseFill
                    ? new SolidColorBrush(MediaColor.FromArgb(60, color.R, color.G, color.B))
                    : System.Windows.Media.Brushes.Transparent,
                RadiusX = Config.CornerRadius,
                RadiusY = Config.CornerRadius,
                Visibility = Visibility.Visible
            };

            double thickness = Config.StrokeThickness;
            if (Config.AllowRandomThickness)
                thickness = _random.NextDouble() * (Config.MaxThickness - Config.MinThickness) + Config.MinThickness;
            rectangle.StrokeThickness = thickness;

            var box = new DrawBox(rectangle);
            RootCanvas.Children.Add(rectangle);
            _boxes.Add(box);
            _activeBox = box;
            _isDrawing = true;

            ApplyEffect(box, Config.EffectsEnabled ? ChooseEffect() : EffectMode.Solid, thickness);
            UpdateRect(box, _start, _start);
            Visibility = Visibility.Visible;
            return true;
        }

        public void EndDraw()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(EndDraw);
                return;
            }

            if (!_isDrawing || _activeBox == null)
                return;

            var box = _activeBox;
            _activeBox = null;
            _isDrawing = false;

            double displaySeconds = Config.BoxDisplaySeconds;
            if (displaySeconds > 0)
            {
                double fadeSeconds = Math.Min(Math.Max(0.05, Config.FadeSeconds), displaySeconds);
                var fade = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    BeginTime = TimeSpan.FromSeconds(displaySeconds - fadeSeconds),
                    Duration = TimeSpan.FromSeconds(fadeSeconds),
                    FillBehavior = FillBehavior.Stop
                };
                fade.Completed += (_, _) => RemoveBox(box);
                box.Rectangle.BeginAnimation(OpacityProperty, fade);
            }

            NativeMethods.ReleaseCapture();
            ToggleOverlayClickThrough(true);
        }

        public void UpdateMouse(System.Drawing.Point currentScreen)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateMouse(currentScreen));
                return;
            }

            if (!_isDrawing || _activeBox == null)
                return;

            var current = ConvertScreenPixelsToWindowDips(currentScreen);
            UpdateRect(_activeBox, _start, current);
        }

        private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WM_HOTKEY)
            {
                if (wParam.ToInt32() == DrawHotkeyId)
                    ArmDraw();
                else if (wParam.ToInt32() == ClearHotkeyId)
                    ClearBoxes();

                handled = true;
            }

            return IntPtr.Zero;
        }

        private bool TryRegisterHotkeys(AppSettings settings, out string error)
        {
            var previousSettings = _settings.Clone();
            var restorePrevious = _hotkeysRegistered;

            if (restorePrevious)
            {
                NativeMethods.UnregisterHotKey(_windowHandle, DrawHotkeyId);
                NativeMethods.UnregisterHotKey(_windowHandle, ClearHotkeyId);
                _hotkeysRegistered = false;
            }

            if (RegisterHotkey(DrawHotkeyId, settings.DrawHotkey, out error))
            {
                if (RegisterHotkey(ClearHotkeyId, settings.ClearHotkey, out error))
                {
                    _hotkeysRegistered = true;
                    return true;
                }

                NativeMethods.UnregisterHotKey(_windowHandle, DrawHotkeyId);
            }

            if (restorePrevious)
            {
                var originalError = error;
                var restoredDraw = RegisterHotkey(DrawHotkeyId, previousSettings.DrawHotkey, out var restoreError);
                var restoredClear = restoredDraw &&
                    RegisterHotkey(ClearHotkeyId, previousSettings.ClearHotkey, out restoreError);

                _hotkeysRegistered = restoredDraw && restoredClear;
                if (!_hotkeysRegistered)
                    error = $"{originalError} The previous shortcuts could not be restored: {restoreError}";
                else
                    error = originalError;
            }

            return false;
        }

        private bool RegisterHotkey(int id, HotkeySettings shortcut, out string error)
        {
            if (NativeMethods.RegisterHotKey(_windowHandle, id, shortcut.Modifiers, shortcut.VirtualKey))
            {
                error = string.Empty;
                return true;
            }

            error = $"Could not register {shortcut.ToDisplayString()}: " +
                new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        private void MakeWindowClickThrough()
        {
            var extendedStyle = NativeMethods.GetWindowLong(_windowHandle, GWL_EXSTYLE);
            extendedStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLong(_windowHandle, GWL_EXSTYLE, extendedStyle);
        }

        private void ToggleOverlayClickThrough(bool enableClickThrough)
        {
            var extendedStyle = NativeMethods.GetWindowLong(_windowHandle, GWL_EXSTYLE);
            if (enableClickThrough)
            {
                extendedStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE;
            }
            else
            {
                extendedStyle &= ~WS_EX_TRANSPARENT;
                extendedStyle |= WS_EX_LAYERED | WS_EX_NOACTIVATE;
            }

            NativeMethods.SetWindowLong(_windowHandle, GWL_EXSTYLE, extendedStyle);
            NativeMethods.SetWindowPos(
                _windowHandle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }

        private System.Windows.Point ConvertScreenPixelsToWindowDips(System.Drawing.Point screenPixelPoint)
        {
            var devicePoint = new System.Windows.Point(screenPixelPoint.X, screenPixelPoint.Y);
            var source = PresentationSource.FromVisual(this);
            System.Windows.Point dipAbsolute;
            if (source != null)
            {
                dipAbsolute = source.CompositionTarget.TransformFromDevice.Transform(devicePoint);
            }
            else
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                dipAbsolute = new System.Windows.Point(
                    devicePoint.X / dpi.DpiScaleX,
                    devicePoint.Y / dpi.DpiScaleY);
            }

            return new System.Windows.Point(dipAbsolute.X - Left, dipAbsolute.Y - Top);
        }

        private EffectMode ChooseEffect()
        {
            if (Config.SelectedEffect != EffectMode.Random)
                return Config.SelectedEffect;

            double solidWeight = Math.Max(0.0, Config.WeightSolid);
            double staticWeight = Math.Max(0.0, Config.WeightDashedStatic);
            double movingWeight = Math.Max(0.0, Config.WeightDashedMoving);
            double totalWeight = solidWeight + staticWeight + movingWeight;
            if (totalWeight <= 0.0)
                return EffectMode.Solid;

            double choice = _random.NextDouble() * totalWeight;
            if (choice < solidWeight)
                return EffectMode.Solid;
            if (choice < solidWeight + staticWeight)
                return EffectMode.DashedStatic;
            return EffectMode.DashedMoving;
        }

        private void ApplyEffect(DrawBox box, EffectMode effect, double thickness)
        {
            var rectangle = box.Rectangle;
            double dashLength = Config.DashLength > 0
                ? Config.DashLength
                : Math.Max(2, thickness * 4);
            double dashGap = Config.DashGap > 0 ? Config.DashGap : dashLength;

            if (effect == EffectMode.Solid)
                return;

            rectangle.StrokeDashArray = new DoubleCollection { dashLength, dashGap };
            if (effect != EffectMode.DashedMoving)
                return;

            var animation = new DoubleAnimation
            {
                From = 0,
                To = dashLength + dashGap,
                Duration = TimeSpan.FromSeconds(Math.Max(0.05, Config.MovingDashSeconds)),
                RepeatBehavior = RepeatBehavior.Forever
            };
            box.DashStoryboard = new Storyboard();
            Storyboard.SetTarget(animation, rectangle);
            Storyboard.SetTargetProperty(animation, new PropertyPath(Shape.StrokeDashOffsetProperty));
            box.DashStoryboard.Children.Add(animation);
            box.DashStoryboard.Begin(this, true);
        }

        private static void UpdateRect(DrawBox box, System.Windows.Point start, System.Windows.Point end)
        {
            double left = Math.Min(start.X, end.X);
            double top = Math.Min(start.Y, end.Y);
            Canvas.SetLeft(box.Rectangle, left);
            Canvas.SetTop(box.Rectangle, top);
            box.Rectangle.Width = Math.Max(1, Math.Abs(start.X - end.X));
            box.Rectangle.Height = Math.Max(1, Math.Abs(start.Y - end.Y));
        }

        private void RemoveBox(DrawBox box)
        {
            if (!_boxes.Remove(box))
                return;

            box.DashStoryboard?.Stop(this);
            box.Rectangle.BeginAnimation(OpacityProperty, null);
            RootCanvas.Children.Remove(box.Rectangle);

            if (ReferenceEquals(_activeBox, box))
            {
                ClearRequested?.Invoke(this, EventArgs.Empty);
                _activeBox = null;
                _isDrawing = false;
                NativeMethods.ReleaseCapture();
                ToggleOverlayClickThrough(true);
            }

            if (_boxes.Count == 0 && !_isDrawing)
                Visibility = Visibility.Hidden;
        }

        private MediaColor RandomColor()
        {
            double hue = _random.NextDouble() * 360.0;
            double saturation = 0.6 + _random.NextDouble() * 0.4;
            double value = 0.7 + _random.NextDouble() * 0.3;
            return ColorFromHSV(hue, saturation, value);
        }

        private static MediaColor ColorFromHSV(double hue, double saturation, double value)
        {
            int sector = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double fraction = hue / 60 - Math.Floor(hue / 60);
            value *= 255;
            byte valueByte = Convert.ToByte(Math.Round(value));
            byte p = Convert.ToByte(Math.Round(value * (1 - saturation)));
            byte q = Convert.ToByte(Math.Round(value * (1 - fraction * saturation)));
            byte t = Convert.ToByte(Math.Round(value * (1 - (1 - fraction) * saturation)));
            return sector switch
            {
                0 => MediaColor.FromRgb(valueByte, t, p),
                1 => MediaColor.FromRgb(q, valueByte, p),
                2 => MediaColor.FromRgb(p, valueByte, t),
                3 => MediaColor.FromRgb(p, q, valueByte),
                4 => MediaColor.FromRgb(t, p, valueByte),
                _ => MediaColor.FromRgb(valueByte, p, q),
            };
        }

        private sealed class DrawBox
        {
            public DrawBox(WpfRectangle rectangle)
            {
                Rectangle = rectangle;
            }

            public WpfRectangle Rectangle { get; }
            public Storyboard? DashStoryboard { get; set; }
        }
    }

    internal static class NativeMethods
    {
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        public static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        public static extern int SetWindowLong(IntPtr hWnd, int index, int newLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
