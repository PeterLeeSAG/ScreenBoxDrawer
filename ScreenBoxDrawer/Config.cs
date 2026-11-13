using System;

namespace ScreenBoxDrawer
{
    public enum EffectMode
    {
        Random = 0,
        Solid = 1,
        DashedStatic = 2,
        DashedMoving = 3
    }

    public static class Config
    {
        // Stroke thickness
        public static double StrokeThickness { get; set; } = 3.0;
        public static bool AllowRandomThickness { get; set; } = false;
        public static double MinThickness { get; set; } = 2.0;
        public static double MaxThickness { get; set; } = 8.0;

        // Fade duration in seconds
        public static double FadeSeconds { get; set; } = 1.5;
        public static double BoxDisplaySeconds { get; set; } = 1.5;

        // Fill (transparent when false)
        public static bool UseFill { get; set; } = false;

        // Corner radius
        public static double CornerRadius { get; set; } = 8.0;

        // Effect selection:
        // - Set SelectedEffect to Random to keep random behavior,
        //   or to Solid/DashedStatic/DashedMoving to force one.
        public static EffectMode SelectedEffect { get; set; } = EffectMode.Solid;
        public static bool EffectsEnabled { get; set; } = true;
        public static int MaximumVisibleBoxes { get; set; } = 10;

        // If SelectedEffect == Random, control probabilities by weight:
        // (weights relative to each other; set zero to disable a type)
        public static double WeightSolid { get; set; } = 1.0;
        public static double WeightDashedStatic { get; set; } = 1.0;
        public static double WeightDashedMoving { get; set; } = 1.0;

        // Dash geometry defaults (used when dashed)
        // If <= 0, code will fall back to thickness-based dash length
        public static double DashLength { get; set; } = 0.0; // px
        public static double DashGap { get; set; } = 0.0; // px

        // Moving dash animation duration in seconds (smaller => faster)
        public static double MovingDashSeconds { get; set; } = 1.2;

        // Random seed for reproducible randomness (null = time-based)
        public static int? RandomSeed { get; set; } = null;

        internal static Random CreateRandom()
        {
            return RandomSeed.HasValue ? new Random(RandomSeed.Value) : new Random();
        }
    }
}
