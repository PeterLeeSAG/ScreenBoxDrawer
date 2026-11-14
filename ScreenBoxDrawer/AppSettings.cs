using System.IO;
using System.Text.Json;
using WpfKey = System.Windows.Input.Key;

namespace ScreenBoxDrawer
{
    public sealed class AppSettings
    {
        public HotkeySettings DrawHotkey { get; set; } = new()
        {
            Control = true,
            Shift = true,
            Key = "D"
        };

        public HotkeySettings ArrowHotkey { get; set; } = new()
        {
            Control = true,
            Shift = true,
            Key = "A"
        };

        public HotkeySettings ClearHotkey { get; set; } = new()
        {
            Control = true,
            Shift = true,
            Key = "X"
        };

        public int MaximumVisibleBoxes { get; set; } = 10;
        public double BoxDisplaySeconds { get; set; } = 1.5;
        public bool EffectsEnabled { get; set; } = true;
        public bool DirectGestureEnabled { get; set; } = true;

        public AppSettings Clone()
        {
            return new AppSettings
            {
                DrawHotkey = DrawHotkey.Clone(),
                ArrowHotkey = ArrowHotkey.Clone(),
                ClearHotkey = ClearHotkey.Clone(),
                MaximumVisibleBoxes = MaximumVisibleBoxes,
                BoxDisplaySeconds = BoxDisplaySeconds,
                EffectsEnabled = EffectsEnabled,
                DirectGestureEnabled = DirectGestureEnabled
            };
        }

        public string? Validate()
        {
            if (MaximumVisibleBoxes < 1 || MaximumVisibleBoxes > 100)
                return "Maximum visible boxes must be between 1 and 100.";

            if (!double.IsFinite(BoxDisplaySeconds) ||
                BoxDisplaySeconds < 0 ||
                BoxDisplaySeconds > TimeSpan.MaxValue.TotalSeconds - 1)
            {
                return "Box display duration must be zero or a finite nonnegative number of seconds.";
            }

            if (DrawHotkey == null || ArrowHotkey == null || ClearHotkey == null)
                return "Draw, arrow, and clear shortcuts must all be configured.";

            if (!DrawHotkey.IsValid)
                return "The box shortcut must include a key and at least one modifier.";

            if (!ArrowHotkey.IsValid)
                return "The arrow shortcut must include a key and at least one modifier.";

            if (!ClearHotkey.IsValid)
                return "The clear shortcut must include a key and at least one modifier.";

            if (DrawHotkey.Matches(ArrowHotkey) ||
                DrawHotkey.Matches(ClearHotkey) ||
                ArrowHotkey.Matches(ClearHotkey))
            {
                return "The box, arrow, and clear shortcuts must be different.";
            }

            return null;
        }
    }

    public sealed class HotkeySettings
    {
        public bool Control { get; set; }
        public bool Alt { get; set; }
        public bool Shift { get; set; }
        public bool Windows { get; set; }
        public string Key { get; set; } = "D";

        public uint Modifiers =>
            (Control ? 0x0002u : 0) |
            (Alt ? 0x0001u : 0) |
            (Shift ? 0x0004u : 0) |
            (Windows ? 0x0008u : 0);

        public uint VirtualKey =>
            Enum.TryParse(Key, true, out WpfKey parsed)
                ? (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(parsed)
                : 0;

        public bool IsValid =>
            Modifiers != 0 &&
            Enum.TryParse(Key, true, out WpfKey parsed) &&
            parsed != WpfKey.None &&
            VirtualKey != 0;

        public HotkeySettings Clone()
        {
            return new HotkeySettings
            {
                Control = Control,
                Alt = Alt,
                Shift = Shift,
                Windows = Windows,
                Key = Key
            };
        }

        public bool Matches(HotkeySettings other)
        {
            return Modifiers == other.Modifiers &&
                string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);
        }

        public string ToDisplayString()
        {
            var parts = new List<string>();
            if (Control) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Windows) parts.Add("Win");

            var keyName = Key;
            if (Key.Length == 2 && Key[0] == 'D' && char.IsDigit(Key[1]))
                keyName = Key[1].ToString();

            parts.Add(keyName);
            return string.Join("+", parts);
        }
    }

    internal static class SettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenBoxDrawer",
            "settings.json");

        public static AppSettings Load()
        {
            if (!File.Exists(FilePath))
                return new AppSettings();

            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? throw new InvalidDataException("The settings file is empty.");
            var validationError = settings.Validate();
            if (validationError != null)
                throw new InvalidDataException(validationError);

            return settings;
        }

        public static void Save(AppSettings settings)
        {
            var validationError = settings.Validate();
            if (validationError != null)
                throw new InvalidDataException(validationError);

            var directory = Path.GetDirectoryName(FilePath)
                ?? throw new InvalidOperationException("The settings directory could not be determined.");
            Directory.CreateDirectory(directory);

            var temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, FilePath, true);
        }
    }
}
