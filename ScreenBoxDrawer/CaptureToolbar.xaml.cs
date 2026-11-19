using System.Windows;
using System.Windows.Controls;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace ScreenBoxDrawer
{
    public partial class CaptureToolbar : WpfUserControl
    {
        public event EventHandler? LineRequested;
        public event EventHandler? BoxRequested;
        public event EventHandler? CircleRequested;
        public event EventHandler? CopyRequested;
        public event EventHandler? CloseRequested;

        public CaptureToolbar()
        {
            InitializeComponent();
        }

        public string StatusText
        {
            get => StatusTextBlock.Text;
            set => StatusTextBlock.Text = value;
        }

        public void SetSelectedTool(CaptureMarkupTool tool)
        {
            LineButton.Background = tool == CaptureMarkupTool.Line
                ? System.Windows.SystemColors.HighlightBrush
                : System.Windows.SystemColors.ControlBrush;
            BoxButton.Background = tool == CaptureMarkupTool.Box
                ? System.Windows.SystemColors.HighlightBrush
                : System.Windows.SystemColors.ControlBrush;
            CircleButton.Background = tool == CaptureMarkupTool.Circle
                ? System.Windows.SystemColors.HighlightBrush
                : System.Windows.SystemColors.ControlBrush;
        }

        private void LineButton_Click(object sender, RoutedEventArgs e)
        {
            LineRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BoxButton_Click(object sender, RoutedEventArgs e)
        {
            BoxRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CircleButton_Click(object sender, RoutedEventArgs e)
        {
            CircleRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            CopyRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public enum CaptureMarkupTool
    {
        None,
        Line,
        Box,
        Circle
    }
}
