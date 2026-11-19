using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using MediaBrush = System.Windows.Media.Brush;
using MediaPen = System.Windows.Media.Pen;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfLine = System.Windows.Shapes.Line;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace ScreenBoxDrawer
{
    public partial class CaptureWindow : Window
    {
        private readonly BitmapSource _desktopImage;
        private readonly DrawingRectangle _screenBounds;
        private readonly int _imagePixelWidth;
        private readonly int _imagePixelHeight;
        private System.Windows.Point _dragStart;
        private DrawingPoint _dragStartPixel;
        private System.Windows.Rect _selection;
        private DrawingRectangle _selectionPixels;
        private Shape? _activeMarkup;
        private bool _isSelecting;
        private bool _isMarking;
        private CapturePhase _phase = CapturePhase.Select;
        private CaptureMarkupTool _markupTool = CaptureMarkupTool.Line;

        public CaptureWindow()
        {
            InitializeComponent();
            Toolbar.LineRequested += (_, _) => SetMarkupTool(CaptureMarkupTool.Line);
            Toolbar.BoxRequested += (_, _) => SetMarkupTool(CaptureMarkupTool.Box);
            Toolbar.CircleRequested += (_, _) => SetMarkupTool(CaptureMarkupTool.Circle);
            Toolbar.CopyRequested += CopyButton_Click;
            Toolbar.CloseRequested += CloseButton_Click;
            (_desktopImage, _screenBounds) = CaptureDesktop();
            _imagePixelWidth = _desktopImage.PixelWidth;
            _imagePixelHeight = _desktopImage.PixelHeight;
            ScreenshotImage.Source = _desktopImage;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            new WindowInteropHelper(this).EnsureHandle();
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var topLeft = source.CompositionTarget.TransformFromDevice.Transform(
                    new System.Windows.Point(_screenBounds.Left, _screenBounds.Top));
                var bottomRight = source.CompositionTarget.TransformFromDevice.Transform(
                    new System.Windows.Point(_screenBounds.Right, _screenBounds.Bottom));
                Left = topLeft.X;
                Top = topLeft.Y;
                Width = bottomRight.X - topLeft.X;
                Height = bottomRight.Y - topLeft.Y;
            }
            else
            {
                Left = SystemParameters.VirtualScreenLeft;
                Top = SystemParameters.VirtualScreenTop;
                Width = SystemParameters.VirtualScreenWidth;
                Height = SystemParameters.VirtualScreenHeight;
            }

            UpdateLayout();
            Activate();
            Topmost = true;
            NativeMethods.SetForegroundWindow(
                new WindowInteropHelper(this).Handle);
            Focus();
            Keyboard.Focus(this);
            UpdateSelectionVisual(new System.Windows.Rect());
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            RootGrid.ReleaseMouseCapture();
        }

        private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsToolbarSource(e.OriginalSource as DependencyObject))
                return;

            if (_phase == CapturePhase.Select)
            {
                _dragStartPixel = GetCursorPosition();
                _dragStart = ClampToWindow(ConvertScreenPixelsToWindowDips(_dragStartPixel));
                _isSelecting = true;
                RootGrid.CaptureMouse();
                UpdateSelectionVisual(CreateBounds(_dragStart, _dragStart));
            }
            else if (_markupTool != CaptureMarkupTool.None)
            {
                _dragStart = ClampToSelection(
                    ConvertScreenPixelsToWindowDips(GetCursorPosition()));
                _isMarking = true;
                _activeMarkup = CreateMarkup(_dragStart);
                MarkupCanvas.Children.Add(_activeMarkup);
                RootGrid.CaptureMouse();
            }

            e.Handled = true;
        }

        private void RootGrid_MouseMove(object sender, WpfMouseEventArgs e)
        {
            if (_isSelecting)
            {
                var currentPixel = GetCursorPosition();
                UpdateSelectionVisual(CreateBounds(
                    _dragStart,
                    ClampToWindow(ConvertScreenPixelsToWindowDips(currentPixel))));
            }
            else if (_isMarking && _activeMarkup != null)
            {
                UpdateMarkup(
                    _activeMarkup,
                    _dragStart,
                    ClampToSelection(ConvertScreenPixelsToWindowDips(GetCursorPosition())));
            }
        }

        private void RootGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSelecting)
            {
                _isSelecting = false;
                RootGrid.ReleaseMouseCapture();
                var currentPixel = GetCursorPosition();
                UpdateSelectionVisual(CreateBounds(
                    _dragStart,
                    ClampToWindow(ConvertScreenPixelsToWindowDips(currentPixel))));
                _selectionPixels = CreateSourcePixelBounds(_dragStartPixel, currentPixel);
                CompleteSelection();
            }
            else if (_isMarking && _activeMarkup != null)
            {
                _isMarking = false;
                RootGrid.ReleaseMouseCapture();
                var end = ClampToSelection(
                    ConvertScreenPixelsToWindowDips(GetCursorPosition()));
                UpdateMarkup(_activeMarkup, _dragStart, end);
                if (IsDegenerateMarkup(_activeMarkup))
                    MarkupCanvas.Children.Remove(_activeMarkup);
                _activeMarkup = null;
            }

            e.Handled = true;
        }

        private void CompleteSelection()
        {
            if (_selection.Width < 4 || _selection.Height < 4)
            {
                UpdateSelectionVisual(new System.Windows.Rect());
                return;
            }

            var sourceRect = _selectionPixels;
            var croppedImage = new CroppedBitmap(
                _desktopImage,
                new Int32Rect(sourceRect.X, sourceRect.Y, sourceRect.Width, sourceRect.Height));
            croppedImage.Freeze();

            SelectedImage.Source = croppedImage;
            Canvas.SetLeft(SelectedImage, _selection.Left);
            Canvas.SetTop(SelectedImage, _selection.Top);
            SelectedImage.Width = _selection.Width;
            SelectedImage.Height = _selection.Height;
            SelectedImage.Visibility = Visibility.Visible;

            MarkupCanvas.Clip = new RectangleGeometry(_selection);
            SelectionBorder.Visibility = Visibility.Visible;
            Toolbar.Visibility = Visibility.Visible;
            Toolbar.StatusText = "Mark the captured image";
            _phase = CapturePhase.Mark;
            SetMarkupTool(CaptureMarkupTool.Line);
            PositionToolbar();
        }

        private Shape CreateMarkup(System.Windows.Point start)
        {
            var stroke = new SolidColorBrush(System.Windows.Media.Colors.Red);
            if (_markupTool == CaptureMarkupTool.Line)
            {
                return new WpfLine
                {
                    Stroke = stroke,
                    StrokeThickness = 3,
                    X1 = start.X,
                    Y1 = start.Y,
                    X2 = start.X,
                    Y2 = start.Y
                };
            }

            return _markupTool == CaptureMarkupTool.Box
                ? new WpfRectangle
                {
                    Stroke = stroke,
                    StrokeThickness = 3,
                    Fill = System.Windows.Media.Brushes.Transparent
                }
                : new WpfEllipse
                {
                    Stroke = stroke,
                    StrokeThickness = 3,
                    Fill = System.Windows.Media.Brushes.Transparent
                };
        }

        private static void UpdateMarkup(
            Shape markup,
            System.Windows.Point start,
            System.Windows.Point end)
        {
            if (markup is WpfLine line)
            {
                line.X1 = start.X;
                line.Y1 = start.Y;
                line.X2 = end.X;
                line.Y2 = end.Y;
                return;
            }

            var left = Math.Min(start.X, end.X);
            var top = Math.Min(start.Y, end.Y);
            Canvas.SetLeft(markup, left);
            Canvas.SetTop(markup, top);
            markup.Width = Math.Abs(start.X - end.X);
            markup.Height = Math.Abs(start.Y - end.Y);
        }

        private static bool IsDegenerateMarkup(Shape markup)
        {
            if (markup is WpfLine line)
                return Math.Abs(line.X2 - line.X1) < 2 && Math.Abs(line.Y2 - line.Y1) < 2;

            return markup.Width < 2 || markup.Height < 2;
        }

        private void SetMarkupTool(CaptureMarkupTool tool)
        {
            _markupTool = tool;
            Toolbar.SetSelectedTool(tool);
        }

        private void CopyButton_Click(object? sender, EventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetImage(RenderSelection());
                Toolbar.StatusText = "Copied to clipboard";
            }
            catch (ExternalException ex)
            {
                System.Windows.MessageBox.Show(
                    $"The captured image could not be copied to the clipboard.\n\n{ex.Message}",
                    "ScreenBoxDrawer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void CloseButton_Click(object? sender, EventArgs e)
        {
            Close();
        }

        private void Window_KeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private BitmapSource RenderSelection()
        {
            var sourceRect = _selectionPixels;
            var croppedImage = new CroppedBitmap(
                _desktopImage,
                new Int32Rect(sourceRect.X, sourceRect.Y, sourceRect.Width, sourceRect.Height));
            croppedImage.Freeze();

            var scaleX = sourceRect.Width / _selection.Width;
            var scaleY = sourceRect.Height / _selection.Height;
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawImage(
                    croppedImage,
                    new System.Windows.Rect(0, 0, sourceRect.Width, sourceRect.Height));

                foreach (var child in MarkupCanvas.Children)
                {
                    if (child is WpfLine line)
                    {
                        var pen = CreatePen(line.Stroke, line.StrokeThickness * scaleX);
                        context.DrawLine(
                            pen,
                            ToImagePoint(line.X1, line.Y1, scaleX, scaleY),
                            ToImagePoint(line.X2, line.Y2, scaleX, scaleY));
                    }
                    else if (child is WpfRectangle rectangle)
                    {
                        var pen = CreatePen(rectangle.Stroke, rectangle.StrokeThickness * scaleX);
                        context.DrawRectangle(
                            null,
                            pen,
                            ToImageRect(rectangle, scaleX, scaleY));
                    }
                    else if (child is WpfEllipse ellipse)
                    {
                        var pen = CreatePen(ellipse.Stroke, ellipse.StrokeThickness * scaleX);
                        var rect = ToImageRect(ellipse, scaleX, scaleY);
                        context.DrawEllipse(null, pen, new System.Windows.Point(
                            rect.Left + rect.Width / 2,
                            rect.Top + rect.Height / 2),
                            rect.Width / 2,
                            rect.Height / 2);
                    }
                }
            }

            var result = new RenderTargetBitmap(
                sourceRect.Width,
                sourceRect.Height,
                96,
                96,
                PixelFormats.Pbgra32);
            result.Render(visual);
            result.Freeze();
            return result;
        }

        private System.Windows.Point ToImagePoint(
            double x,
            double y,
            double scaleX,
            double scaleY)
        {
            return new System.Windows.Point(
                (x - _selection.Left) * scaleX,
                (y - _selection.Top) * scaleY);
        }

        private System.Windows.Rect ToImageRect(Shape shape, double scaleX, double scaleY)
        {
            var left = Canvas.GetLeft(shape);
            var top = Canvas.GetTop(shape);
            return new System.Windows.Rect(
                (left - _selection.Left) * scaleX,
                (top - _selection.Top) * scaleY,
                shape.Width * scaleX,
                shape.Height * scaleY);
        }

        private static MediaPen CreatePen(MediaBrush? brush, double thickness)
        {
            return new MediaPen(
                brush ?? System.Windows.Media.Brushes.Red,
                Math.Max(1, thickness));
        }

        private void UpdateSelectionVisual(System.Windows.Rect selection)
        {
            _selection = selection;
            if (selection.Width <= 0 || selection.Height <= 0)
            {
                SelectionBorder.Visibility = Visibility.Collapsed;
                return;
            }

            Canvas.SetLeft(SelectionBorder, selection.Left);
            Canvas.SetTop(SelectionBorder, selection.Top);
            SelectionBorder.Width = selection.Width;
            SelectionBorder.Height = selection.Height;
            SelectionBorder.Visibility = Visibility.Visible;
        }

        private void PositionToolbar()
        {
            const double edgeMargin = 8;
            const double gap = 12;

            if (RootGrid.ActualWidth <= 0 || RootGrid.ActualHeight <= 0)
                return;

            Toolbar.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            Toolbar.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            Toolbar.Margin = new Thickness(0);
            Toolbar.Measure(new System.Windows.Size(
                double.PositiveInfinity,
                double.PositiveInfinity));

            var toolbarWidth = Toolbar.DesiredSize.Width;
            var toolbarHeight = Toolbar.DesiredSize.Height;
            var x = _selection.Left + (_selection.Width - toolbarWidth) / 2;
            var y = _selection.Bottom + gap;

            var selectionCenter = new DrawingPoint(
                _selectionPixels.X + _selectionPixels.Width / 2,
                _selectionPixels.Y + _selectionPixels.Height / 2);
            var monitor = FindMonitorContaining(selectionCenter);
            var monitorTopLeft = ConvertScreenPixelsToWindowDips(
                new DrawingPoint(monitor.Left, monitor.Top));
            var monitorBottomRight = ConvertScreenPixelsToWindowDips(
                new DrawingPoint(monitor.Right, monitor.Bottom));
            var monitorBounds = new System.Windows.Rect(
                monitorTopLeft,
                monitorBottomRight);

            if (y + toolbarHeight > monitorBounds.Bottom - edgeMargin)
                y = _selection.Top - toolbarHeight - gap;

            if (y < monitorBounds.Top + edgeMargin)
            {
                x = _selection.Right + gap;
                y = _selection.Top;
                if (x + toolbarWidth > monitorBounds.Right - edgeMargin)
                    x = _selection.Left - toolbarWidth - gap;
            }

            var maxX = Math.Max(
                monitorBounds.Left + edgeMargin,
                monitorBounds.Right - toolbarWidth - edgeMargin);
            var maxY = Math.Max(
                monitorBounds.Top + edgeMargin,
                monitorBounds.Bottom - toolbarHeight - edgeMargin);
            x = Math.Clamp(
                x,
                monitorBounds.Left + edgeMargin,
                maxX);
            y = Math.Clamp(
                y,
                monitorBounds.Top + edgeMargin,
                maxY);
            Toolbar.Margin = new Thickness(x, y, 0, 0);
        }

        private System.Windows.Point ClampToWindow(System.Windows.Point point)
        {
            return new System.Windows.Point(
                Math.Clamp(point.X, 0, RootGrid.ActualWidth),
                Math.Clamp(point.Y, 0, RootGrid.ActualHeight));
        }

        private System.Windows.Point ClampToSelection(System.Windows.Point point)
        {
            return new System.Windows.Point(
                Math.Clamp(point.X, _selection.Left, _selection.Right),
                Math.Clamp(point.Y, _selection.Top, _selection.Bottom));
        }

        private DrawingRectangle CreateSourcePixelBounds(
            DrawingPoint first,
            DrawingPoint second)
        {
            var left = Math.Clamp(
                Math.Min(first.X, second.X) - _screenBounds.Left,
                0,
                _imagePixelWidth - 1);
            var top = Math.Clamp(
                Math.Min(first.Y, second.Y) - _screenBounds.Top,
                0,
                _imagePixelHeight - 1);
            var right = Math.Clamp(
                Math.Max(first.X, second.X) - _screenBounds.Left,
                left + 1,
                _imagePixelWidth);
            var bottom = Math.Clamp(
                Math.Max(first.Y, second.Y) - _screenBounds.Top,
                top + 1,
                _imagePixelHeight);
            return new DrawingRectangle(left, top, right - left, bottom - top);
        }

        private DrawingPoint GetCursorPosition()
        {
            if (NativeMethods.GetCursorPos(out var point))
                return point;

            var fallback = Mouse.GetPosition(RootGrid);
            var absolute = ConvertWindowDipsToScreenPixels(fallback);
            return new DrawingPoint(absolute.X, absolute.Y);
        }

        private System.Windows.Point ConvertScreenPixelsToWindowDips(
            DrawingPoint screenPixelPoint)
        {
            var source = PresentationSource.FromVisual(RootGrid);
            if (source?.CompositionTarget != null)
            {
                var dipAbsolute = source.CompositionTarget.TransformFromDevice.Transform(
                    new System.Windows.Point(screenPixelPoint.X, screenPixelPoint.Y));
                return new System.Windows.Point(
                    dipAbsolute.X - Left,
                    dipAbsolute.Y - Top);
            }

            var dpi = VisualTreeHelper.GetDpi(RootGrid);
            return new System.Windows.Point(
                screenPixelPoint.X / dpi.DpiScaleX - Left,
                screenPixelPoint.Y / dpi.DpiScaleY - Top);
        }

        private DrawingPoint ConvertWindowDipsToScreenPixels(
            System.Windows.Point windowPoint)
        {
            var source = PresentationSource.FromVisual(RootGrid);
            if (source?.CompositionTarget != null)
            {
                var deviceAbsolute = source.CompositionTarget.TransformToDevice.Transform(
                    new System.Windows.Point(Left + windowPoint.X, Top + windowPoint.Y));
                return new DrawingPoint(
                    (int)Math.Round(deviceAbsolute.X),
                    (int)Math.Round(deviceAbsolute.Y));
            }

            var dpi = VisualTreeHelper.GetDpi(RootGrid);
            return new DrawingPoint(
                (int)Math.Round((Left + windowPoint.X) * dpi.DpiScaleX),
                (int)Math.Round((Top + windowPoint.Y) * dpi.DpiScaleY));
        }

        private static System.Windows.Rect CreateBounds(
            System.Windows.Point first,
            System.Windows.Point second)
        {
            return new System.Windows.Rect(
                new System.Windows.Point(
                    Math.Min(first.X, second.X),
                    Math.Min(first.Y, second.Y)),
                new System.Windows.Point(
                    Math.Max(first.X, second.X),
                    Math.Max(first.Y, second.Y)));
        }

        private bool IsToolbarSource(DependencyObject? source)
        {
            while (source != null)
            {
                if (ReferenceEquals(source, Toolbar))
                    return true;
                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private static (BitmapSource Image, DrawingRectangle Bounds) CaptureDesktop()
        {
            var monitorBounds = GetDisplayMonitorBounds();
            var bounds = monitorBounds.Count > 0
                ? CreateUnionBounds(monitorBounds)
                : GetVirtualScreenBoundsFallback();
            using var bitmap = new Bitmap(
                bounds.Width,
                bounds.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    bounds.Left,
                    bounds.Top,
                    0,
                    0,
                    bitmap.Size,
                    CopyPixelOperation.SourceCopy);
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return (image, bounds);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private static List<DrawingRectangle> GetDisplayMonitorBounds()
        {
            var bounds = new List<DrawingRectangle>();
            NativeMethods.EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                (_, _, monitorRectPointer, _) =>
                {
                    if (monitorRectPointer != IntPtr.Zero)
                    {
                        var nativeRect =
                            Marshal.PtrToStructure<NativeRect>(monitorRectPointer);
                        bounds.Add(DrawingRectangle.FromLTRB(
                            nativeRect.Left,
                            nativeRect.Top,
                            nativeRect.Right,
                            nativeRect.Bottom));
                    }
                    return true;
                },
                IntPtr.Zero);
            return bounds;
        }

        private static DrawingRectangle CreateUnionBounds(
            IReadOnlyCollection<DrawingRectangle> monitorBounds)
        {
            var left = monitorBounds.Min(rectangle => rectangle.Left);
            var top = monitorBounds.Min(rectangle => rectangle.Top);
            var right = monitorBounds.Max(rectangle => rectangle.Right);
            var bottom = monitorBounds.Max(rectangle => rectangle.Bottom);
            return DrawingRectangle.FromLTRB(left, top, right, bottom);
        }

        private static DrawingRectangle GetVirtualScreenBoundsFallback()
        {
            return DrawingRectangle.FromLTRB(
                NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
                NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
                NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN) +
                    NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
                NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN) +
                    NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));
        }

        private static DrawingRectangle FindMonitorContaining(DrawingPoint point)
        {
            var monitors = GetDisplayMonitorBounds();
            foreach (var monitor in monitors)
            {
                if (monitor.Contains(point))
                    return monitor;
            }

            DrawingRectangle? nearest = null;
            var nearestDistance = double.PositiveInfinity;
            foreach (var monitor in monitors)
            {
                var centerX = monitor.Left + monitor.Width / 2.0;
                var centerY = monitor.Top + monitor.Height / 2.0;
                var dx = point.X - centerX;
                var dy = point.Y - centerY;
                var distance = dx * dx + dy * dy;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = monitor;
                }
            }

            return nearest ?? GetVirtualScreenBoundsFallback();
        }

        private enum CapturePhase
        {
            Select,
            Mark
        }

    }
}
