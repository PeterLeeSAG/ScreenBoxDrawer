using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
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
using WpfPath = System.Windows.Shapes.Path;
using WpfPolyline = System.Windows.Shapes.Polyline;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;

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
        private WpfTextBox? _activeTextBox;
        private bool _isSelecting;
        private bool _isMarking;
        private CapturePhase _phase = CapturePhase.Select;
        private CaptureMarkupTool _markupTool = CaptureMarkupTool.Line;
        private double _drawingThickness = 3;
        private double _textFontSize = 16;
        private System.Windows.Media.Color _captureColor =
            System.Windows.Media.Colors.Red;

        public CaptureWindow()
        {
            InitializeComponent();
            Toolbar.LineRequested += (_, _) => SetMarkupTool(CaptureMarkupTool.Line);
            Toolbar.ArrowRequested += (_, _) => SetMarkupTool(CaptureMarkupTool.Arrow);
            Toolbar.BoxRequested += (_, _) => SetMarkupTool(CaptureMarkupTool.Box);
            Toolbar.CircleRequested += (_, _) => SetMarkupTool(CaptureMarkupTool.Circle);
            Toolbar.BrushRequested += (_, _) => SetMarkupTool(CaptureMarkupTool.Brush);
            Toolbar.TextRequested += (_, _) => SetMarkupTool(CaptureMarkupTool.Text);
            Toolbar.UndoRequested += UndoMarkup;
            Toolbar.CopyRequested += CopyButton_Click;
            Toolbar.CloseRequested += CloseButton_Click;
            Toolbar.FinishRequested += FinishButton_Click;
            Toolbar.ThicknessChanged += (_, args) =>
            {
                _drawingThickness = args.Thickness;
                Toolbar.StatusText = $"Drawing thickness: {args.Thickness:0}";
            };
            Toolbar.FontSizeChanged += (_, args) =>
            {
                _textFontSize = args.FontSize;
                if (_activeTextBox != null)
                    _activeTextBox.FontSize = args.FontSize;
                Toolbar.StatusText = $"Text size: {args.FontSize:0} pt";
            };
            Toolbar.ColorChanged += (_, args) =>
            {
                _captureColor = args.Color;
                if (_activeTextBox != null)
                    _activeTextBox.Foreground =
                        new SolidColorBrush(args.Color);
                Toolbar.StatusText = "Color changed";
            };
            (_desktopImage, _screenBounds) = CaptureDesktop();
            _imagePixelWidth = _desktopImage.PixelWidth;
            _imagePixelHeight = _desktopImage.PixelHeight;
            ScreenshotImage.Source = _desktopImage;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            new WindowInteropHelper(this).EnsureHandle();
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

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

            if (IsActiveTextEntrySource(e.OriginalSource as DependencyObject))
                return;

            if (_phase == CapturePhase.Select)
            {
                _dragStart = ClampToWindow(e.GetPosition(RootGrid));
                _dragStartPixel = GetCursorPosition();
                _isSelecting = true;
                RootGrid.CaptureMouse();
                UpdateSelectionVisual(CreateBounds(_dragStart, _dragStart));
            }
            else if (_markupTool != CaptureMarkupTool.None)
            {
                _dragStart = ClampToSelection(e.GetPosition(RootGrid));
                if (_markupTool == CaptureMarkupTool.Text)
                {
                    BeginTextEntry(_dragStart);
                    e.Handled = true;
                    return;
                }

                _isMarking = true;
                _activeMarkup = CreateMarkup(_dragStart);
                MarkupCanvas.Children.Add(_activeMarkup);
                if (_activeMarkup is WpfPolyline brush)
                    brush.Points.Add(_dragStart);
                RootGrid.CaptureMouse();
            }

            e.Handled = true;
        }

        private void RootGrid_MouseMove(object sender, WpfMouseEventArgs e)
        {
            if (_isSelecting)
            {
                UpdateSelectionVisual(CreateBounds(
                    _dragStart,
                    ClampToWindow(e.GetPosition(RootGrid))));
            }
            else if (_isMarking && _activeMarkup != null)
            {
                var end = ClampToSelection(e.GetPosition(RootGrid));
                if (_activeMarkup is WpfPolyline brush)
                {
                    if (brush.Points.Count == 0 ||
                        Distance(brush.Points[^1], end) >= 1)
                    {
                        brush.Points.Add(end);
                    }
                }
                else
                {
                    UpdateMarkup(_activeMarkup, _dragStart, end);
                }
            }
        }

        private void RootGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSelecting)
            {
                _isSelecting = false;
                RootGrid.ReleaseMouseCapture();
                UpdateSelectionVisual(CreateBounds(
                    _dragStart,
                    ClampToWindow(e.GetPosition(RootGrid))));
                _selectionPixels = CreateSourcePixelBounds(
                    _dragStartPixel,
                    GetCursorPosition());
                CompleteSelection();
            }
            else if (_isMarking && _activeMarkup != null)
            {
                _isMarking = false;
                RootGrid.ReleaseMouseCapture();
                var end = ClampToSelection(e.GetPosition(RootGrid));
                if (_activeMarkup is WpfPolyline brush)
                {
                    if (brush.Points.Count == 0 ||
                        Distance(brush.Points[^1], end) >= 1)
                    {
                        brush.Points.Add(end);
                    }
                }
                else
                {
                    UpdateMarkup(_activeMarkup, _dragStart, end);
                }

                if (IsDegenerateMarkup(_activeMarkup, end))
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
            var stroke = new SolidColorBrush(_captureColor);
            if (_markupTool == CaptureMarkupTool.Line)
            {
                return new WpfLine
                {
                    Stroke = stroke,
                    StrokeThickness = _drawingThickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    X1 = start.X,
                    Y1 = start.Y,
                    X2 = start.X,
                    Y2 = start.Y
                };
            }

            if (_markupTool == CaptureMarkupTool.Arrow)
            {
                return new WpfPath
                {
                    Stroke = stroke,
                    Fill = stroke,
                    StrokeThickness = _drawingThickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Data = CreateArrowGeometry(start, start, _drawingThickness)
                };
            }

            if (_markupTool == CaptureMarkupTool.Box)
            {
                return new WpfRectangle
                {
                    Stroke = stroke,
                    StrokeThickness = _drawingThickness,
                    Fill = System.Windows.Media.Brushes.Transparent
                };
            }

            if (_markupTool == CaptureMarkupTool.Circle)
            {
                return new WpfEllipse
                {
                    Stroke = stroke,
                    StrokeThickness = _drawingThickness,
                    Fill = System.Windows.Media.Brushes.Transparent
                };
            }

            var brushStroke = new WpfPolyline
            {
                Stroke = stroke,
                StrokeThickness = _drawingThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = System.Windows.Media.Brushes.Transparent
            };
            return brushStroke;
        }

        private static Geometry CreateArrowGeometry(
            System.Windows.Point start,
            System.Windows.Point end,
            double thickness)
        {
            var deltaX = end.X - start.X;
            var deltaY = end.Y - start.Y;
            var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (length < 0.001)
                return Geometry.Empty;

            var unitX = deltaX / length;
            var unitY = deltaY / length;
            var normalX = -unitY;
            var normalY = unitX;
            var headLength = Math.Clamp(thickness * 4, 10, 24);
            headLength = Math.Min(headLength, length * 0.45);
            if (headLength < 1)
                headLength = 1;

            var basePoint = new System.Windows.Point(
                end.X - unitX * headLength,
                end.Y - unitY * headLength);
            var wingOffset = headLength * 0.48;
            var wingOne = new System.Windows.Point(
                basePoint.X + normalX * wingOffset,
                basePoint.Y + normalY * wingOffset);
            var wingTwo = new System.Windows.Point(
                basePoint.X - normalX * wingOffset,
                basePoint.Y - normalY * wingOffset);

            var group = new GeometryGroup();
            group.Children.Add(new LineGeometry(start, end));

            var figure = new PathFigure
            {
                StartPoint = end,
                IsClosed = true,
                IsFilled = true
            };
            figure.Segments.Add(new LineSegment(wingOne, true));
            figure.Segments.Add(new LineSegment(wingTwo, true));
            group.Children.Add(new PathGeometry(
                new[] { figure },
                FillRule.Nonzero,
                null));
            return group;
        }

        private static double Distance(
            System.Windows.Point first,
            System.Windows.Point second)
        {
            var deltaX = second.X - first.X;
            var deltaY = second.Y - first.Y;
            return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        }

        private void UpdateMarkup(
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

            if (markup is WpfPath arrow)
            {
                arrow.Data = CreateArrowGeometry(start, end, _drawingThickness);
                return;
            }

            if (markup is WpfPolyline brush)
            {
                if (!brush.Points.Contains(end))
                    brush.Points.Add(end);
                return;
            }

            var left = Math.Min(start.X, end.X);
            var top = Math.Min(start.Y, end.Y);
            Canvas.SetLeft(markup, left);
            Canvas.SetTop(markup, top);
            markup.Width = Math.Abs(start.X - end.X);
            markup.Height = Math.Abs(start.Y - end.Y);
        }

        private bool IsDegenerateMarkup(Shape markup, System.Windows.Point end)
        {
            if (markup is WpfLine line)
                return Math.Abs(line.X2 - line.X1) < 2 && Math.Abs(line.Y2 - line.Y1) < 2;

            if (markup is WpfPath)
                return Distance(_dragStart, end) < 2;

            if (markup is WpfPolyline brush)
            {
                if (brush.Points.Count < 2)
                    return true;

                var first = brush.Points[0];
                var last = brush.Points[^1];
                return Distance(first, last) < 1 &&
                    brush.Points.All(point => Distance(first, point) < 1);
            }

            return markup.Width < 2 || markup.Height < 2;
        }

        private void SetMarkupTool(CaptureMarkupTool tool)
        {
            if (tool != CaptureMarkupTool.Text)
                CommitActiveTextEntry();

            _markupTool = tool;
            Toolbar.SetSelectedTool(tool);
        }

        private void BeginTextEntry(System.Windows.Point start)
        {
            CommitActiveTextEntry();

            var textBox = new WpfTextBox
            {
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = _textFontSize,
                Foreground = new SolidColorBrush(_captureColor),
                Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(230, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(_captureColor),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                MinWidth = 80,
                MaxWidth = Math.Max(80, _selection.Right - start.X),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                AcceptsTab = false
            };

            Canvas.SetLeft(textBox, start.X);
            Canvas.SetTop(textBox, start.Y);
            MarkupCanvas.Children.Add(textBox);
            _activeTextBox = textBox;

            textBox.KeyDown += TextEntry_KeyDown;
            textBox.LostKeyboardFocus += TextEntry_LostKeyboardFocus;
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    textBox.Focus();
                    textBox.CaretIndex = textBox.Text.Length;
                    textBox.SelectAll();
                }),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        private void TextEntry_KeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                    return;

                CommitActiveTextEntry();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelActiveTextEntry();
                e.Handled = true;
            }
        }

        private void TextEntry_LostKeyboardFocus(
            object sender,
            KeyboardFocusChangedEventArgs e)
        {
            if (sender is WpfTextBox textBox &&
                ReferenceEquals(textBox, _activeTextBox))
            {
                CommitActiveTextEntry();
            }
        }

        private void CommitActiveTextEntry()
        {
            if (_activeTextBox == null)
                return;

            var textBox = _activeTextBox;
            _activeTextBox = null;

            var text = textBox.Text.TrimEnd('\r', '\n');
            var index = MarkupCanvas.Children.IndexOf(textBox);
            MarkupCanvas.Children.Remove(textBox);

            if (text.Length > 0)
            {
                var textBlock = new WpfTextBlock
                {
                    Text = text,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    FontSize = textBox.FontSize,
                    FontWeight = FontWeights.Normal,
                    Foreground = new SolidColorBrush(_captureColor),
                    TextWrapping = TextWrapping.NoWrap,
                    MaxWidth = Math.Max(40, _selection.Right - Canvas.GetLeft(textBox))
                };
                Canvas.SetLeft(textBlock, Canvas.GetLeft(textBox));
                Canvas.SetTop(textBlock, Canvas.GetTop(textBox));
                if (index >= 0 && index <= MarkupCanvas.Children.Count)
                    MarkupCanvas.Children.Insert(index, textBlock);
                else
                    MarkupCanvas.Children.Add(textBlock);
            }
        }

        private void CancelActiveTextEntry()
        {
            if (_activeTextBox == null)
                return;

            var textBox = _activeTextBox;
            _activeTextBox = null;
            MarkupCanvas.Children.Remove(textBox);
        }

        private void UndoMarkup(object? sender, EventArgs e)
        {
            if (_activeTextBox != null)
            {
                CancelActiveTextEntry();
                Toolbar.StatusText = "Undid text";
                return;
            }

            if (_isMarking && _activeMarkup != null)
            {
                MarkupCanvas.Children.Remove(_activeMarkup);
                _activeMarkup = null;
                _isMarking = false;
                RootGrid.ReleaseMouseCapture();
                Toolbar.StatusText = "Undid markup";
                return;
            }

            if (MarkupCanvas.Children.Count > 0)
            {
                MarkupCanvas.Children.RemoveAt(
                    MarkupCanvas.Children.Count - 1);
                Toolbar.StatusText = "Undid markup";
            }
        }

        private bool TryCopySelection()
        {
            try
            {
                System.Windows.Clipboard.SetImage(RenderSelection());
                Toolbar.StatusText = "Copied to clipboard";
                return true;
            }
            catch (ExternalException ex)
            {
                System.Windows.MessageBox.Show(
                    $"The captured image could not be copied to the clipboard.\n\n{ex.Message}",
                    "ScreenBoxDrawer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
        }

        private void CopyButton_Click(object? sender, EventArgs e)
        {
            TryCopySelection();
        }

        private void FinishButton_Click(object? sender, EventArgs e)
        {
            CommitActiveTextEntry();
            if (TryCopySelection())
                Close();
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
                    if (child is WpfTextBlock textBlock)
                    {
                        var textBrush = textBlock.Foreground as MediaBrush
                            ?? System.Windows.Media.Brushes.Black;
                        var typeface = new Typeface(
                            textBlock.FontFamily,
                            textBlock.FontStyle,
                            textBlock.FontWeight,
                            textBlock.FontStretch);
                        var formattedText = new FormattedText(
                            textBlock.Text,
                            CultureInfo.CurrentCulture,
                            textBlock.FlowDirection,
                            typeface,
                            textBlock.FontSize * scaleY,
                            textBrush,
                            VisualTreeHelper.GetDpi(this).PixelsPerDip);
                        context.DrawText(
                            formattedText,
                            ToImagePoint(
                                Canvas.GetLeft(textBlock),
                                Canvas.GetTop(textBlock),
                                scaleX,
                                scaleY));
                    }
                    else if (child is WpfPolyline brush)
                    {
                        var pen = CreatePen(
                            brush.Stroke,
                            brush.StrokeThickness * scaleX);
                        for (var index = 1; index < brush.Points.Count; index++)
                        {
                            context.DrawLine(
                                pen,
                                ToImagePoint(
                                    brush.Points[index - 1].X,
                                    brush.Points[index - 1].Y,
                                    scaleX,
                                    scaleY),
                                ToImagePoint(
                                    brush.Points[index].X,
                                    brush.Points[index].Y,
                                    scaleX,
                                    scaleY));
                        }
                    }
                    else if (child is WpfPath arrow)
                    {
                        var pen = CreatePen(
                            arrow.Stroke,
                            arrow.StrokeThickness * scaleX);
                        var geometry = arrow.Data?.Clone();
                        if (geometry != null)
                        {
                            geometry.Transform = new MatrixTransform(
                                scaleX,
                                0,
                                0,
                                scaleY,
                                -_selection.Left * scaleX,
                                -_selection.Top * scaleY);
                            context.DrawGeometry(
                                arrow.Fill,
                                pen,
                                geometry);
                        }
                    }
                    else if (child is WpfLine line)
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
            Toolbar.Visibility = Visibility.Visible;
            Toolbar.IsHitTestVisible = true;
            Toolbar.InvalidateMeasure();
            Toolbar.InvalidateArrange();
            Toolbar.Measure(new System.Windows.Size(
                double.PositiveInfinity,
                double.PositiveInfinity));
            Toolbar.UpdateLayout();

            var toolbarWidth = Toolbar.DesiredSize.Width;
            var toolbarHeight = Toolbar.DesiredSize.Height;
            if (toolbarWidth <= 0)
                toolbarWidth = Toolbar.ActualWidth;
            if (toolbarHeight <= 0)
                toolbarHeight = Toolbar.ActualHeight;
            if (toolbarWidth <= 0)
                toolbarWidth = 760;
            if (toolbarHeight <= 0)
                toolbarHeight = 88;

            var x = _selection.Left + (_selection.Width - toolbarWidth) / 2;
            var y = _selection.Bottom + gap;

            if (y + toolbarHeight <= RootGrid.ActualHeight - edgeMargin)
            {
                // First choice: below the capture area.
            }
            else
            {
                var aboveY = _selection.Top - toolbarHeight - gap;
                if (aboveY >= edgeMargin)
                {
                    y = aboveY;
                }
                else
                {
                    var rightX = _selection.Right + gap;
                    if (rightX + toolbarWidth <= RootGrid.ActualWidth - edgeMargin)
                    {
                        x = rightX;
                        y = _selection.Top;
                    }
                    else
                    {
                        var leftX = _selection.Left - toolbarWidth - gap;
                        if (leftX >= edgeMargin)
                        {
                            x = leftX;
                            y = _selection.Top;
                        }
                        else
                        {
                            x = _selection.Left +
                                (_selection.Width - toolbarWidth) / 2;
                            y = _selection.Top +
                                (_selection.Height - toolbarHeight) / 2;
                        }
                    }
                }
            }

            var maxX = Math.Max(
                edgeMargin,
                RootGrid.ActualWidth - toolbarWidth - edgeMargin);
            var maxY = Math.Max(
                edgeMargin,
                RootGrid.ActualHeight - toolbarHeight - edgeMargin);
            x = Math.Clamp(x, edgeMargin, maxX);
            y = Math.Clamp(y, edgeMargin, maxY);

            Toolbar.Margin = new Thickness(x, y, 0, 0);
            Toolbar.Visibility = Visibility.Visible;
            Toolbar.IsHitTestVisible = true;
            Toolbar.UpdateLayout();

            var finalToolbarRect = new System.Windows.Rect(
                x,
                y,
                toolbarWidth,
                toolbarHeight);
            if (finalToolbarRect.Right > RootGrid.ActualWidth)
            {
                Toolbar.Margin = new Thickness(
                    Math.Max(edgeMargin, RootGrid.ActualWidth - toolbarWidth - edgeMargin),
                    y,
                    0,
                    0);
            }
            if (finalToolbarRect.Bottom > RootGrid.ActualHeight)
            {
                Toolbar.Margin = new Thickness(
                    Toolbar.Margin.Left,
                    Math.Max(edgeMargin, RootGrid.ActualHeight - toolbarHeight - edgeMargin),
                    0,
                    0);
            }
            Toolbar.Visibility = Visibility.Visible;
        }

        private DrawingPoint GetSelectionCenterPixel()
        {
            var left = Math.Min(
                _selectionPixels.Left,
                _selectionPixels.Right);
            var top = Math.Min(
                _selectionPixels.Top,
                _selectionPixels.Bottom);
            var right = Math.Max(
                _selectionPixels.Left,
                _selectionPixels.Right);
            var bottom = Math.Max(
                _selectionPixels.Top,
                _selectionPixels.Bottom);

            return new DrawingPoint(
                _screenBounds.Left + left + (right - left) / 2,
                _screenBounds.Top + top + (bottom - top) / 2);
        }

        private static DrawingRectangle FindMonitorContaining(
            DrawingPoint pixel)
        {
            var monitors = GetDisplayMonitorBounds();
            foreach (var monitor in monitors)
            {
                if (monitor.Contains(pixel))
                    return monitor;
            }

            DrawingRectangle nearest = CreateUnionBounds(monitors);
            var nearestDistance = double.PositiveInfinity;
            foreach (var monitor in monitors)
            {
                var centerX = monitor.Left + monitor.Width / 2.0;
                var centerY = monitor.Top + monitor.Height / 2.0;
                var dx = pixel.X - centerX;
                var dy = pixel.Y - centerY;
                var distance = dx * dx + dy * dy;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = monitor;
                }
            }

            return nearest;
        }

        private System.Windows.Rect ConvertMonitorRectToWindowDips(
            DrawingRectangle monitor)
        {
            var left = (monitor.Left - _screenBounds.Left) *
                RootGrid.ActualWidth / _imagePixelWidth;
            var top = (monitor.Top - _screenBounds.Top) *
                RootGrid.ActualHeight / _imagePixelHeight;
            var right = (monitor.Right - _screenBounds.Left) *
                RootGrid.ActualWidth / _imagePixelWidth;
            var bottom = (monitor.Bottom - _screenBounds.Top) *
                RootGrid.ActualHeight / _imagePixelHeight;
            return new System.Windows.Rect(
                new System.Windows.Point(left, top),
                new System.Windows.Point(right, bottom));
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
            var rootWidth = Math.Max(RootGrid.ActualWidth, 1);
            var rootHeight = Math.Max(RootGrid.ActualHeight, 1);
            return new DrawingPoint(
                (int)Math.Round(
                    _screenBounds.Left +
                    fallback.X * _imagePixelWidth / rootWidth),
                (int)Math.Round(
                    _screenBounds.Top +
                    fallback.Y * _imagePixelHeight / rootHeight));
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

        private bool IsActiveTextEntrySource(DependencyObject? source)
        {
            if (_activeTextBox == null)
                return false;

            while (source != null)
            {
                if (ReferenceEquals(source, _activeTextBox))
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

        private enum CapturePhase
        {
            Select,
            Mark
        }

    }
}
