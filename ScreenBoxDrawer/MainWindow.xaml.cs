using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using MediaColor = System.Windows.Media.Color;
using WpfPath = System.Windows.Shapes.Path;
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
        private const int ArrowHotkeyId = 2;
        private const int ClearHotkeyId = 3;

        private readonly Random _random;
        private readonly List<DrawBox> _boxes = new();
        private AppSettings _settings;
        private HwndSource? _windowSource;
        private IntPtr _windowHandle;
        private System.Windows.Point _start;
        private DrawBox? _activeBox;
        private bool _isDrawing;
        private bool _hasMoved;
        private DrawingTool _armedTool;
        private bool _hotkeysRegistered;

        public event EventHandler? ClearRequested;

        public MainWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings.Clone();
            _random = Config.CreateRandom();
        }

        public bool IsDrawingArmed => _armedTool != DrawingTool.None;

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

            Hide();
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
            Config.DirectGestureEnabled = _settings.DirectGestureEnabled;

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
                _armedTool = DrawingTool.Box;
        }

        public void ArmArrow()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(ArmArrow);
                return;
            }

            if (!_isDrawing)
                _armedTool = DrawingTool.Arrow;
        }

        public void ClearBoxes()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(ClearBoxes);
                return;
            }

            ClearRequested?.Invoke(this, EventArgs.Empty);
            var pendingBox = _activeBox;
            _armedTool = DrawingTool.None;
            _isDrawing = false;
            _activeBox = null;
            _hasMoved = false;
            Hide();

            if (pendingBox != null && !_boxes.Contains(pendingBox))
                DisposeShape(pendingBox);

            foreach (var box in _boxes.ToArray())
            {
                DisposeShape(box);
                RootCanvas.Children.Remove(box.Shape);
            }

            _boxes.Clear();
            RootCanvas.Children.Clear();
            RootCanvas.InvalidateVisual();
            UpdateLayout();
            NativeMethods.ReleaseCapture();
            ToggleOverlayClickThrough(true);
        }

        public bool BeginDraw(System.Drawing.Point startScreen)
        {
            return Dispatcher.Invoke(() => BeginDrawCore(startScreen));
        }

        private bool BeginDrawCore(System.Drawing.Point startScreen)
        {
            if (_isDrawing)
                return false;

            if (_boxes.Count == 0 && RootCanvas.Children.Count > 0)
                RootCanvas.Children.Clear();

            var tool = _armedTool == DrawingTool.None ? DrawingTool.Box : _armedTool;
            ToggleOverlayClickThrough(false);
            NativeMethods.SetCapture(_windowHandle);
            _armedTool = DrawingTool.None;
            _start = ConvertScreenPixelsToWindowDips(startScreen);

            var color = RandomColor();
            Shape shape = tool == DrawingTool.Box
                ? new WpfRectangle
                {
                    RadiusX = Config.CornerRadius,
                    RadiusY = Config.CornerRadius
                }
                : new WpfPath { Stretch = Stretch.None };
            shape.Stroke = new SolidColorBrush(color);
            shape.Fill = Config.UseFill
                ? new SolidColorBrush(MediaColor.FromArgb(60, color.R, color.G, color.B))
                : System.Windows.Media.Brushes.Transparent;
            shape.Visibility = Visibility.Collapsed;

            double thickness = Config.StrokeThickness;
            if (Config.AllowRandomThickness)
                thickness = _random.NextDouble() * (Config.MaxThickness - Config.MinThickness) + Config.MinThickness;
            shape.StrokeThickness = thickness;

            var box = new DrawBox(shape, tool);
            _activeBox = box;
            _isDrawing = true;
            _hasMoved = false;
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

            if (!_hasMoved)
            {
                _hasMoved = false;
                DisposeShape(box);
                NativeMethods.ReleaseCapture();
                ToggleOverlayClickThrough(true);
                Hide();
                return;
            }

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
                box.Shape.BeginAnimation(OpacityProperty, fade);
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
            if (!_hasMoved &&
                Math.Abs(current.X - _start.X) < 2 &&
                Math.Abs(current.Y - _start.Y) < 2)
            {
                return;
            }

            if (!_hasMoved)
                ActivateDrawing(_activeBox);

            UpdateDrawing(_activeBox, _start, current);
        }

        private void ActivateDrawing(DrawBox box)
        {
            while (_boxes.Count >= Config.MaximumVisibleBoxes)
                RemoveBox(_boxes[0]);

            RootCanvas.Children.Add(box.Shape);
            _boxes.Add(box);
            box.Shape.Visibility = Visibility.Visible;
            ApplyEffect(
                box,
                Config.EffectsEnabled ? ChooseEffect() : EffectMode.Solid,
                box.Shape.StrokeThickness);
            UpdateDrawing(box, _start, _start);
            if (!IsVisible)
                Show();
            Visibility = Visibility.Visible;
            _hasMoved = true;
        }

        private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WM_HOTKEY)
            {
                if (wParam.ToInt32() == DrawHotkeyId)
                    ArmDraw();
                else if (wParam.ToInt32() == ArrowHotkeyId)
                    ArmArrow();
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
                UnregisterHotkeys();

            if (RegisterHotkeySet(settings, out error))
            {
                _hotkeysRegistered = true;
                return true;
            }

            if (restorePrevious)
            {
                var originalError = error;
                if (RegisterHotkeySet(previousSettings, out var restoreError))
                {
                    _hotkeysRegistered = true;
                    error = originalError;
                }
                else
                {
                    _hotkeysRegistered = false;
                    error = $"{originalError} The previous shortcuts could not be restored: {restoreError}";
                }
            }

            return false;
        }

        private bool RegisterHotkeySet(AppSettings settings, out string error)
        {
            if (RegisterHotkey(DrawHotkeyId, settings.DrawHotkey, out error) &&
                RegisterHotkey(ArrowHotkeyId, settings.ArrowHotkey, out error) &&
                RegisterHotkey(ClearHotkeyId, settings.ClearHotkey, out error))
            {
                error = string.Empty;
                return true;
            }

            UnregisterHotkeys();
            return false;
        }

        private void UnregisterHotkeys()
        {
            if (_windowHandle == IntPtr.Zero)
                return;

            NativeMethods.UnregisterHotKey(_windowHandle, DrawHotkeyId);
            NativeMethods.UnregisterHotKey(_windowHandle, ArrowHotkeyId);
            NativeMethods.UnregisterHotKey(_windowHandle, ClearHotkeyId);
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
            var shape = box.Shape;
            double dashLength = Config.DashLength > 0
                ? Config.DashLength
                : Math.Max(2, thickness * 4);
            double dashGap = Config.DashGap > 0 ? Config.DashGap : dashLength;

            if (effect == EffectMode.Solid)
                return;

            shape.StrokeDashArray = new DoubleCollection { dashLength, dashGap };
            if (effect != EffectMode.DashedMoving)
                return;

            var animation = new DoubleAnimation
            {
                From = 0,
                To = -(dashLength + dashGap),
                Duration = TimeSpan.FromSeconds(Math.Max(0.05, Config.MovingDashSeconds)),
                RepeatBehavior = RepeatBehavior.Forever
            };
            box.DashStoryboard = new Storyboard();
            Storyboard.SetTarget(animation, shape);
            Storyboard.SetTargetProperty(animation, new PropertyPath(Shape.StrokeDashOffsetProperty));
            box.DashStoryboard.Children.Add(animation);
            box.DashStoryboard.Begin(this, true);
        }

        private static void UpdateDrawing(DrawBox box, System.Windows.Point start, System.Windows.Point end)
        {
            if (box.Tool == DrawingTool.Box && box.Shape is WpfRectangle rectangle)
            {
                double left = Math.Min(start.X, end.X);
                double top = Math.Min(start.Y, end.Y);
                Canvas.SetLeft(rectangle, left);
                Canvas.SetTop(rectangle, top);
                rectangle.Width = Math.Max(1, Math.Abs(start.X - end.X));
                rectangle.Height = Math.Max(1, Math.Abs(start.Y - end.Y));
            }
            else if (box.Tool == DrawingTool.Arrow && box.Shape is WpfPath path)
            {
                path.Data = CreateArrowGeometry(start, end, path.StrokeThickness, out var bounds);
                Canvas.SetLeft(path, bounds.Left);
                Canvas.SetTop(path, bounds.Top);
            }
        }

        private static StreamGeometry CreateArrowGeometry(
            System.Windows.Point start,
            System.Windows.Point end,
            double strokeThickness,
            out System.Windows.Rect bounds)
        {
            double deltaX = end.X - start.X;
            double deltaY = end.Y - start.Y;
            double length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            var geometry = new StreamGeometry();
            if (length < 1)
            {
                bounds = new System.Windows.Rect(start, start);
                geometry.Freeze();
                return geometry;
            }

            double directionX = deltaX / length;
            double directionY = deltaY / length;
            double headLength = Math.Min(length * 0.5, Math.Max(10, strokeThickness * 4));
            double halfHeadWidth = headLength * 0.55;
            var headBase = new System.Windows.Point(
                end.X - directionX * headLength,
                end.Y - directionY * headLength);
            var perpendicular = new System.Windows.Point(-directionY, directionX);
            var firstWing = new System.Windows.Point(
                headBase.X + perpendicular.X * halfHeadWidth,
                headBase.Y + perpendicular.Y * halfHeadWidth);
            var secondWing = new System.Windows.Point(
                headBase.X - perpendicular.X * halfHeadWidth,
                headBase.Y - perpendicular.Y * halfHeadWidth);
            bounds = new System.Windows.Rect(start, end);
            bounds.Union(firstWing);
            bounds.Union(secondWing);
            var origin = bounds.TopLeft;

            System.Windows.Point ToLocal(System.Windows.Point point)
            {
                return new System.Windows.Point(point.X - origin.X, point.Y - origin.Y);
            }

            using (var context = geometry.Open())
            {
                context.BeginFigure(ToLocal(start), false, false);
                context.LineTo(ToLocal(end), true, false);
                context.BeginFigure(ToLocal(end), false, false);
                context.LineTo(ToLocal(firstWing), true, false);
                context.BeginFigure(ToLocal(end), false, false);
                context.LineTo(ToLocal(secondWing), true, false);
            }

            geometry.Freeze();
            return geometry;
        }

        private void RemoveBox(DrawBox box)
        {
            if (!_boxes.Remove(box))
                return;

            DisposeShape(box);
            RootCanvas.Children.Remove(box.Shape);

            if (ReferenceEquals(_activeBox, box))
            {
                ClearRequested?.Invoke(this, EventArgs.Empty);
                _activeBox = null;
                _isDrawing = false;
                _armedTool = DrawingTool.None;
                NativeMethods.ReleaseCapture();
                ToggleOverlayClickThrough(true);
            }

            if (_boxes.Count == 0 && !_isDrawing)
                Hide();
        }

        private void DisposeShape(DrawBox box)
        {
            box.DashStoryboard?.Remove(this);
            box.Shape.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            box.Shape.BeginAnimation(OpacityProperty, null);
            box.Shape.Opacity = 0;
            box.Shape.Visibility = Visibility.Collapsed;
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
            public DrawBox(Shape shape, DrawingTool tool)
            {
                Shape = shape;
                Tool = tool;
            }

            public Shape Shape { get; }
            public DrawingTool Tool { get; }
            public Storyboard? DashStoryboard { get; set; }
        }

        private enum DrawingTool
        {
            None,
            Box,
            Arrow
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
