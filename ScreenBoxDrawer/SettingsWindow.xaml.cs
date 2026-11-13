using System.IO;
using System.Globalization;
using System.Windows;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKeyboard = System.Windows.Input.Keyboard;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;

namespace ScreenBoxDrawer
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _originalSettings;
        private readonly MainWindow _overlay;
        private HotkeySettings _drawHotkey;
        private HotkeySettings _arrowHotkey;
        private HotkeySettings _clearHotkey;

        public SettingsWindow(AppSettings settings, MainWindow overlay)
        {
            InitializeComponent();
            _originalSettings = settings.Clone();
            _overlay = overlay;
            _drawHotkey = settings.DrawHotkey.Clone();
            _arrowHotkey = settings.ArrowHotkey.Clone();
            _clearHotkey = settings.ClearHotkey.Clone();

            DrawHotkeyBox.Text = _drawHotkey.ToDisplayString();
            ArrowHotkeyBox.Text = _arrowHotkey.ToDisplayString();
            ClearHotkeyBox.Text = _clearHotkey.ToDisplayString();
            MaximumBoxesTextBox.Text = settings.MaximumVisibleBoxes.ToString(CultureInfo.InvariantCulture);
            BoxDisplaySecondsTextBox.Text = settings.BoxDisplaySeconds.ToString(CultureInfo.InvariantCulture);
            EffectsCheckBox.IsChecked = settings.EffectsEnabled;
        }

        public AppSettings? SavedSettings { get; private set; }

        private void DrawHotkeyBox_PreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            e.Handled = true;
            if (TryCaptureShortcut(e, out var shortcut))
            {
                _drawHotkey = shortcut;
                DrawHotkeyBox.Text = shortcut.ToDisplayString();
                ErrorTextBlock.Text = string.Empty;
            }
            else
            {
                ErrorTextBlock.Text = "Use a shortcut with at least one modifier (Ctrl, Alt, Shift, or Win).";
            }
        }

        private void ClearHotkeyBox_PreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            e.Handled = true;
            if (TryCaptureShortcut(e, out var shortcut))
            {
                _clearHotkey = shortcut;
                ClearHotkeyBox.Text = shortcut.ToDisplayString();
                ErrorTextBlock.Text = string.Empty;
            }
            else
            {
                ErrorTextBlock.Text = "Use a shortcut with at least one modifier (Ctrl, Alt, Shift, or Win).";
            }
        }

        private void ArrowHotkeyBox_PreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            e.Handled = true;
            if (TryCaptureShortcut(e, out var shortcut))
            {
                _arrowHotkey = shortcut;
                ArrowHotkeyBox.Text = shortcut.ToDisplayString();
                ErrorTextBlock.Text = string.Empty;
            }
            else
            {
                ErrorTextBlock.Text = "Use a shortcut with at least one modifier (Ctrl, Alt, Shift, or Win).";
            }
        }

        private void ClearBoxes_Click(object sender, RoutedEventArgs e)
        {
            _overlay.ClearBoxes();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(
                    MaximumBoxesTextBox.Text,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int maximumBoxes) ||
                maximumBoxes < 1 ||
                maximumBoxes > 100)
            {
                ErrorTextBlock.Text = "Maximum visible boxes must be a whole number between 1 and 100.";
                return;
            }

            if (!double.TryParse(
                    BoxDisplaySecondsTextBox.Text,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out double boxDisplaySeconds) ||
                !double.IsFinite(boxDisplaySeconds) ||
                boxDisplaySeconds < 0 ||
                boxDisplaySeconds > TimeSpan.MaxValue.TotalSeconds - 1)
            {
                ErrorTextBlock.Text = "Visible duration must be zero (unlimited) or a nonnegative number of seconds.";
                return;
            }

            var candidate = new AppSettings
            {
                DrawHotkey = _drawHotkey.Clone(),
                ArrowHotkey = _arrowHotkey.Clone(),
                ClearHotkey = _clearHotkey.Clone(),
                MaximumVisibleBoxes = maximumBoxes,
                BoxDisplaySeconds = boxDisplaySeconds,
                EffectsEnabled = EffectsCheckBox.IsChecked == true
            };
            var validationError = candidate.Validate();
            if (validationError != null)
            {
                ErrorTextBlock.Text = validationError;
                return;
            }

            if (!_overlay.TryApplySettings(candidate, out var applyError))
            {
                ErrorTextBlock.Text = applyError;
                return;
            }

            try
            {
                SettingsStore.Save(candidate);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                var rollbackSucceeded = _overlay.TryApplySettings(_originalSettings, out var rollbackError);
                ErrorTextBlock.Text = rollbackSucceeded
                    ? $"Could not save settings: {ex.Message}"
                    : $"Could not save settings: {ex.Message} Previous settings could not be restored: {rollbackError}";
                return;
            }

            SavedSettings = candidate;
            DialogResult = true;
        }

        private static bool TryCaptureShortcut(WpfKeyEventArgs e, out HotkeySettings shortcut)
        {
            var key = e.Key == WpfKey.System ? e.SystemKey : e.Key;
            var modifiers = WpfKeyboard.Modifiers;
            if (key == WpfKey.None ||
                key == WpfKey.System ||
                key == WpfKey.LeftCtrl ||
                key == WpfKey.RightCtrl ||
                key == WpfKey.LeftAlt ||
                key == WpfKey.RightAlt ||
                key == WpfKey.LeftShift ||
                key == WpfKey.RightShift ||
                key == WpfKey.LWin ||
                key == WpfKey.RWin ||
                modifiers == WpfModifierKeys.None)
            {
                shortcut = new HotkeySettings();
                return false;
            }

            shortcut = new HotkeySettings
            {
                Control = modifiers.HasFlag(WpfModifierKeys.Control),
                Alt = modifiers.HasFlag(WpfModifierKeys.Alt),
                Shift = modifiers.HasFlag(WpfModifierKeys.Shift),
                Windows = modifiers.HasFlag(WpfModifierKeys.Windows),
                Key = key.ToString()
            };
            return shortcut.IsValid;
        }
    }
}
