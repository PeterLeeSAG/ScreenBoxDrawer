using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfButton = System.Windows.Controls.Button;
using MediaColor = System.Windows.Media.Color;

namespace ScreenBoxDrawer
{
    public partial class CaptureToolbar : WpfUserControl
    {
        public event EventHandler? LineRequested;
        public event EventHandler? ArrowRequested;
        public event EventHandler? BoxRequested;
        public event EventHandler? CircleRequested;
        public event EventHandler? BrushRequested;
        public event EventHandler? MosaicRequested;
        public event EventHandler? TextRequested;
        public event EventHandler? UndoRequested;
        public event EventHandler? CopyRequested;
        public event EventHandler? SaveRequested;
        public event EventHandler? SaveAsRequested;
        public event EventHandler? CloseRequested;
        public event EventHandler? FinishRequested;
        public event EventHandler<CaptureThicknessChangedEventArgs>? ThicknessChanged;
        public event EventHandler<CaptureFontSizeChangedEventArgs>? FontSizeChanged;
        public event EventHandler<CaptureColorChangedEventArgs>? ColorChanged;
        public event EventHandler? FontSizeMenuRequested;

        public CaptureToolbar()
        {
            InitializeComponent();
            DrawingThickness = 3;
            TextFontSize = 16;
            SelectedColor = System.Windows.Media.Colors.Red;
            ConfigureToolbarToolTips();
        }

        public string StatusText
        {
            get => ToolTip as string ?? string.Empty;
            set => ToolTip = value;
        }

        public double DrawingThickness { get; private set; }
        public double TextFontSize { get; private set; }
        public MediaColor SelectedColor { get; private set; }

        public WpfButton FontSizeButtonControl => FontSizeButton;

        public void SetFontSize(double fontSize)
        {
            TextFontSize = fontSize;
            FontSizeButtonText.Text = $"{fontSize:0} pt";
            FontSizeChanged?.Invoke(
                this,
                new CaptureFontSizeChangedEventArgs(fontSize));
        }

        public void SetSelectedTool(CaptureMarkupTool tool)
        {
            SetToolButtonSelected(LineButton, tool == CaptureMarkupTool.Line);
            SetToolButtonSelected(ArrowButton, tool == CaptureMarkupTool.Arrow);
            SetToolButtonSelected(BoxButton, tool == CaptureMarkupTool.Box);
            SetToolButtonSelected(CircleButton, tool == CaptureMarkupTool.Circle);
            SetToolButtonSelected(BrushButton, tool == CaptureMarkupTool.Brush);
            SetToolButtonSelected(MosaicButton, tool == CaptureMarkupTool.Mosaic);
            SetToolButtonSelected(TextButton, tool == CaptureMarkupTool.Text);
        }

        public void SetSelectedColor(MediaColor color)
        {
            SelectedColor = color;
            foreach (var button in ColorPalette.Children.OfType<WpfButton>())
            {
                var matches = GetColor(button.Tag as string) == color;
                button.BorderBrush = matches
                    ? new SolidColorBrush(MediaColor.FromRgb(25, 118, 210))
                    : System.Windows.Media.Brushes.Transparent;
                button.BorderThickness = new Thickness(matches ? 2 : 2);
            }
        }

        private static void SetToolButtonSelected(WpfButton button, bool selected)
        {
            button.Background = selected
                ? new SolidColorBrush(MediaColor.FromRgb(221, 237, 255))
                : System.Windows.Media.Brushes.Transparent;
            button.BorderBrush = selected
                ? new SolidColorBrush(MediaColor.FromRgb(25, 118, 210))
                : System.Windows.Media.Brushes.Transparent;
            button.BorderThickness = new Thickness(selected ? 2 : 1);
        }

        private void ConfigureToolbarToolTips()
        {
            ConfigureLocalTooltip(BoxButton, "Box");
            ConfigureLocalTooltip(CircleButton, "Circle");
            ConfigureLocalTooltip(ArrowButton, "Arrow");
            ConfigureLocalTooltip(LineButton, "Line");
            ConfigureLocalTooltip(BrushButton, "Brush");
            ConfigureLocalTooltip(MosaicButton, "Mosaic");
            ConfigureLocalTooltip(TextButton, "Text");
            ConfigureLocalTooltip(UndoButton, "Undo");
            ConfigureLocalTooltip(CopyIconButton, "Copy to clipboard");
            ConfigureLocalTooltip(SaveButton, "Save");
            ConfigureLocalTooltip(SaveAsButton, "Save As");
            ConfigureLocalTooltip(CloseButton, "Close");
            ConfigureLocalTooltip(FinishButton, "Copy and close");
            ConfigureLocalTooltip(AppIconButton, "Copy to clipboard");

            foreach (var button in ColorPalette.Children.OfType<WpfButton>())
            {
                var name = button.Tag as string;
                if (!string.IsNullOrWhiteSpace(name))
                    ConfigureLocalTooltip(button, name);
            }
        }

        private void ConfigureLocalTooltip(WpfButton button, string text)
        {
            button.ToolTip = null;
            button.MouseEnter += (_, _) =>
            {
                CustomTooltipText.Text = text;
                CustomTooltip.Margin = new Thickness(0);
                CustomTooltip.Visibility = Visibility.Visible;
                CustomTooltip.Measure(
                    new System.Windows.Size(
                        double.PositiveInfinity,
                        double.PositiveInfinity));

                var position = button.TranslatePoint(
                    new System.Windows.Point(button.ActualWidth / 2, 0),
                    ToolbarRoot);
                var x = position.X - CustomTooltip.DesiredSize.Width / 2;
                var y = position.Y - CustomTooltip.DesiredSize.Height - 6;

                x = Math.Clamp(x, 4, Math.Max(
                    4,
                    ToolbarRoot.ActualWidth - CustomTooltip.DesiredSize.Width - 4));
                y = Math.Clamp(y, 4, Math.Max(
                    4,
                    ToolbarRoot.ActualHeight - CustomTooltip.DesiredSize.Height - 4));

                CustomTooltip.Margin = new Thickness(x, y, 0, 0);
                CustomTooltip.Visibility = Visibility.Visible;
            };
            button.MouseLeave += (_, _) =>
            {
                CustomTooltip.Visibility = Visibility.Collapsed;
            };
        }

        private void LineButton_Click(object sender, RoutedEventArgs e)
        {
            LineRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ArrowButton_Click(object sender, RoutedEventArgs e)
        {
            ArrowRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BoxButton_Click(object sender, RoutedEventArgs e)
        {
            BoxRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CircleButton_Click(object sender, RoutedEventArgs e)
        {
            CircleRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BrushButton_Click(object sender, RoutedEventArgs e)
        {
            BrushRequested?.Invoke(this, EventArgs.Empty);
        }

        private void MosaicButton_Click(object sender, RoutedEventArgs e)
        {
            MosaicRequested?.Invoke(this, EventArgs.Empty);
        }

        private void TextButton_Click(object sender, RoutedEventArgs e)
        {
            TextRequested?.Invoke(this, EventArgs.Empty);
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            UndoRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            CopyRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SaveAsButton_Click(object sender, RoutedEventArgs e)
        {
            SaveAsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            FinishRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ThicknessSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            DrawingThickness = e.NewValue;
            ThicknessChanged?.Invoke(
                this,
                new CaptureThicknessChangedEventArgs(e.NewValue));
        }

        private void FontSizeButton_Click(object sender, RoutedEventArgs e)
        {
            FontSizeMenuRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not WpfButton button)
                return;

            var color = GetColor(button.Tag as string);
            SetSelectedColor(color);
            ColorChanged?.Invoke(
                this,
                new CaptureColorChangedEventArgs(color));
        }

        private static MediaColor GetColor(string? name)
        {
            return name switch
            {
                "Yellow" => MediaColor.FromRgb(255, 214, 0),
                "Blue" => MediaColor.FromRgb(22, 135, 248),
                "Green" => MediaColor.FromRgb(54, 168, 82),
                "Black" => MediaColor.FromRgb(32, 32, 32),
                "Gray" => MediaColor.FromRgb(158, 158, 158),
                "White" => System.Windows.Media.Colors.White,
                _ => MediaColor.FromRgb(255, 32, 32)
            };
        }
    }

    public enum CaptureMarkupTool
    {
        None,
        Line,
        Arrow,
        Box,
        Circle,
        Brush,
        Mosaic,
        Text
    }

    public sealed class CaptureThicknessChangedEventArgs : EventArgs
    {
        public CaptureThicknessChangedEventArgs(double thickness)
        {
            Thickness = thickness;
        }

        public double Thickness { get; }
    }

    public sealed class CaptureFontSizeChangedEventArgs : EventArgs
    {
        public CaptureFontSizeChangedEventArgs(double fontSize)
        {
            FontSize = fontSize;
        }

        public double FontSize { get; }
    }

    public sealed class CaptureColorChangedEventArgs : EventArgs
    {
        public CaptureColorChangedEventArgs(MediaColor color)
        {
            Color = color;
        }

        public MediaColor Color { get; }
    }
}
