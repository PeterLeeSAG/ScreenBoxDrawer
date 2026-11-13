// add to the top of the file with other using directives if not already present
using System.Runtime.InteropServices;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ScreenBoxDrawer
{
    public partial class MainWindow : Window
    {
        // Put these inside the MainWindow class (or in your NativeMethods class)
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_FRAMECHANGED = 0x0020;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        // Optional: Set/Release capture if you want explicit capture (overlay covers whole screen so not required)
        [DllImport("user32.dll")]
        private static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private System.Windows.Point _start;
        private bool _isDrawing = false;
        private readonly Random _rnd;
        private Storyboard? _dashStoryboard;
        private int _sessionId = 0;

        public MainWindow()
        {
            InitializeComponent();
            _rnd = Config.CreateRandom();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            ex |= NativeMethods.WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex);

            MakeWindowClickThrough(hwnd);
        }

        private void MakeWindowClickThrough(IntPtr hwnd)
        {
            var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            ex |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex);
        }
        // Helper: convert screen pixel point (mouse hook) -> WPF point in window coordinates (DIPs)
        private System.Windows.Point ConvertScreenPixelsToWindowDips(System.Drawing.Point screenPixelPoint)
        {
            // screenPixel (pixels) -> WPF device independent units (DIPs)
            var devicePoint = new System.Windows.Point(screenPixelPoint.X, screenPixelPoint.Y);

            // If we have a PresentationSource, use its TransformFromDevice
            var source = PresentationSource.FromVisual(this);
            System.Windows.Point dipAbsolute;
            if (source != null)
            {
                var transform = source.CompositionTarget.TransformFromDevice;
                dipAbsolute = transform.Transform(devicePoint);
            }
            else
            {
                // fallback: use VisualTreeHelper.GetDpi to get scale factor
                var dpi = VisualTreeHelper.GetDpi(this);
                dipAbsolute = new System.Windows.Point(devicePoint.X / dpi.DpiScaleX, devicePoint.Y / dpi.DpiScaleY);
            }

            // Convert absolute DIPs (relative to virtual screen origin) to coordinates relative to this window
            // Window.Left/Top are in DIPs, so subtract them.
            var relative = new System.Windows.Point(dipAbsolute.X - this.Left, dipAbsolute.Y - this.Top);
            return relative;
        }

        public void BeginDraw(System.Drawing.Point startScreen)
        {
            Dispatcher.Invoke(() =>
            {
                // disable click-through so overlay will receive mouse if needed
                ToggleOverlayClickThrough(false);

                // capture the mouse to this overlay so it receives mouse messages reliably
                var hwnd = new WindowInteropHelper(this).EnsureHandle();
                SetCapture(hwnd);

                _sessionId++;

                // Cancel any ongoing fade and reset opacity
                SelectionRect.BeginAnimation(OpacityProperty, null);
                SelectionRect.Opacity = 1.0;

                // Stop previous dash animation
                _dashStoryboard?.Stop(this);
                _dashStoryboard = null;
                SelectionRect.StrokeDashArray = null;
                SelectionRect.StrokeDashOffset = 0;

                // Apply corner radius from config
                SelectionRect.RadiusX = Config.CornerRadius;
                SelectionRect.RadiusY = Config.CornerRadius;

                // Convert the incoming pixel coords -> DIPs relative to the overlay window
                _start = ConvertScreenPixelsToWindowDips(startScreen);

                // Choose color and stroke thickness as before
                var color = RandomColor();
                var brush = new SolidColorBrush(color);
                SelectionRect.Stroke = brush;

                if (Config.UseFill)
                {
                    var fillColor = Color.FromArgb(60, color.R, color.G, color.B);
                    SelectionRect.Fill = new SolidColorBrush(fillColor);
                }
                else
                {
                    SelectionRect.Fill = Brushes.Transparent;
                }

                double thickness = Config.StrokeThickness;
                if (Config.AllowRandomThickness)
                    thickness = _rnd.NextDouble() * (Config.MaxThickness - Config.MinThickness) + Config.MinThickness;

                SelectionRect.StrokeThickness = thickness;

                // choose effect
                EffectMode chosen;
                if (Config.SelectedEffect != EffectMode.Random)
                {
                    chosen = Config.SelectedEffect;
                }
                else
                {
                    double wS = Math.Max(0.0, Config.WeightSolid);
                    double wDs = Math.Max(0.0, Config.WeightDashedStatic);
                    double wDm = Math.Max(0.0, Config.WeightDashedMoving);
                    double total = wS + wDs + wDm;
                    if (total <= 0.0) chosen = EffectMode.Solid;
                    else
                    {
                        double r = _rnd.NextDouble() * total;
                        if (r < wS) chosen = EffectMode.Solid;
                        else if (r < wS + wDs) chosen = EffectMode.DashedStatic;
                        else chosen = EffectMode.DashedMoving;
                    }
                }

                ApplyEffect((int)chosen, thickness);

                SelectionRect.Visibility = Visibility.Visible;
                UpdateRect(_start, _start);
                _isDrawing = true;
                if (Visibility != Visibility.Visible)
                    Visibility = Visibility.Visible;

                // DO NOT re-enable click-through here — it must remain disabled until the draw finishes.
            });
        }

        public void EndDraw()
        {
            Dispatcher.Invoke(() =>
            {
                if (!_isDrawing) return;
                _isDrawing = false;

                int sessionAtFadeStart = _sessionId;

                var fade = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromSeconds(Math.Max(0.05, Config.FadeSeconds)),
                    FillBehavior = FillBehavior.Stop
                };

                fade.Completed += (s, e) =>
                {
                    if (sessionAtFadeStart != _sessionId)
                        return;

                    SelectionRect.Visibility = Visibility.Collapsed;
                    SelectionRect.Opacity = 1.0;
                    SelectionRect.StrokeDashArray = null;
                    SelectionRect.StrokeDashOffset = 0;
                    _dashStoryboard?.Stop(this);
                    _dashStoryboard = null;
                    Visibility = Visibility.Hidden;

                    // release capture and re-enable click-through now that fade finished
                    try
                    {
                        var hwnd = new WindowInteropHelper(this).EnsureHandle();
                        ReleaseCapture();
                    }
                    catch { }

                    // re-enable click-through now that the selection/fade finished
                    ToggleOverlayClickThrough(true);
                };

                SelectionRect.BeginAnimation(OpacityProperty, fade);
            });
        }

        private void ToggleOverlayClickThrough(bool enableClickThrough)
        {
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);

            if (enableClickThrough)
            {
                // add transparent so clicks go through
                ex |= WS_EX_TRANSPARENT;
                ex |= WS_EX_LAYERED; // keep layered (if you used it)
                ex |= WS_EX_NOACTIVATE;
            }
            else
            {
                // remove transparent to receive mouse
                ex &= ~WS_EX_TRANSPARENT;
                ex |= WS_EX_LAYERED; // keep layered
                ex |= WS_EX_NOACTIVATE;
            }

            SetWindowLong(hwnd, GWL_EXSTYLE, ex);

            // force Windows to re-evaluate the styles immediately
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }

        public void UpdateMouse(System.Drawing.Point currentScreen)
        {
            if (!_isDrawing) return;
            Dispatcher.Invoke(() =>
            {
                var cur = ConvertScreenPixelsToWindowDips(currentScreen);
                UpdateRect(_start, cur);
            });
        }

        private void ApplyEffect(int effect, double thickness)
        {
            _dashStoryboard?.Stop(this);
            _dashStoryboard = null;
            SelectionRect.StrokeDashArray = null;
            SelectionRect.StrokeDashOffset = 0;

            // compute dash length/gap (use config values if positive, else based on thickness)
            double dashLen = Config.DashLength > 0 ? Config.DashLength : Math.Max(2, thickness * 4);
            double dashGap = Config.DashGap > 0 ? Config.DashGap : dashLen;

            if (effect == (int)EffectMode.Solid)
            {
                SelectionRect.StrokeDashArray = null;
            }
            else if (effect == (int)EffectMode.DashedStatic)
            {
                SelectionRect.StrokeDashArray = new DoubleCollection() { dashLen, dashGap };
            }
            else // moving dashed
            {
                SelectionRect.StrokeDashArray = new DoubleCollection() { dashLen, dashGap };

                // animate dash offset to create moving effect
                var anim = new DoubleAnimation
                {
                    From = 0,
                    To = dashLen + dashGap,
                    Duration = TimeSpan.FromSeconds(Math.Max(0.05, Config.MovingDashSeconds)),
                    RepeatBehavior = RepeatBehavior.Forever
                };

                _dashStoryboard = new Storyboard();
                Storyboard.SetTarget(anim, SelectionRect);
                Storyboard.SetTargetProperty(anim, new PropertyPath(Shape.StrokeDashOffsetProperty));
                _dashStoryboard.Children.Add(anim);
                _dashStoryboard.Begin(this, true);
            }
        }

        private void UpdateRect(System.Windows.Point a, System.Windows.Point b)
        {
            double left = Math.Min(a.X, b.X);
            double top = Math.Min(a.Y, b.Y);
            double w = Math.Abs(a.X - b.X);
            double h = Math.Abs(a.Y - b.Y);

            Canvas.SetLeft(SelectionRect, left);
            Canvas.SetTop(SelectionRect, top);
            SelectionRect.Width = Math.Max(1, w);
            SelectionRect.Height = Math.Max(1, h);
        }

        private Color RandomColor()
        {
            double h = _rnd.NextDouble() * 360.0;
            double s = 0.6 + _rnd.NextDouble() * 0.4;
            double v = 0.7 + _rnd.NextDouble() * 0.3;
            return ColorFromHSV(h, s, v);
        }

        private static Color ColorFromHSV(double h, double s, double v)
        {
            int hi = Convert.ToInt32(Math.Floor(h / 60)) % 6;
            double f = h / 60 - Math.Floor(h / 60);
            v = v * 255;
            byte vb = Convert.ToByte(Math.Round(v));
            byte p = Convert.ToByte(Math.Round(v * (1 - s)));
            byte q = Convert.ToByte(Math.Round(v * (1 - f * s)));
            byte t = Convert.ToByte(Math.Round(v * (1 - (1 - f) * s)));
            return hi switch
            {
                0 => Color.FromRgb(vb, t, p),
                1 => Color.FromRgb(q, vb, p),
                2 => Color.FromRgb(p, vb, t),
                3 => Color.FromRgb(p, q, vb),
                4 => Color.FromRgb(t, p, vb),
                _ => Color.FromRgb(vb, p, q),
            };
        }
    }

    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}