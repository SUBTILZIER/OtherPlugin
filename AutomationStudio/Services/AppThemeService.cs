using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;

namespace AutomationStudioWpf.Services;

public static class AppThemeService
{
    public static event EventHandler? ThemeChanged;

    private static readonly Dictionary<string, string> DarkPalette = new()
    {
        ["AppBackgroundBrush"] = "#171C24",
        ["PanelBackgroundBrush"] = "#1C232D",
        ["PanelAltBackgroundBrush"] = "#222A35",
        ["BorderBrushDark"] = "#303A48",
        ["ForegroundPrimaryBrush"] = "#E6EDF5",
        ["ForegroundSecondaryBrush"] = "#AAB6C6",
        ["AccentForegroundBrush"] = "#FFFFFF",
        ["InputBackgroundBrush"] = "#232B36",
        ["InputBorderBrush"] = "#3C4A5C",
        ["DisabledInputBackgroundBrush"] = "#1A2028",
        ["DisabledInputBorderBrush"] = "#303A48",
        ["DisabledForegroundBrush"] = "#7C8796",
        ["DropdownBackgroundBrush"] = "#151B23",
        ["DropdownHoverBrush"] = "#203044",
        ["DropdownSelectedBrush"] = "#2D6FB7",
        ["DropdownBorderBrush"] = "#344154",
        ["DropdownTextBrush"] = "#EAF1FA",
        ["DropdownMutedTextBrush"] = "#AAB6C6",
        ["ToolbarButtonBackgroundBrush"] = "#171E28",
        ["ToolbarButtonBorderBrush"] = "#303A48",
        ["CompileDirtyBackgroundBrush"] = "#4A3216",
        ["CompileDirtyBorderBrush"] = "#D68A22",
        ["StatusMutedBrush"] = "#8793A4",
        ["LogErrorBrush"] = "#FF6B6B",
        ["EditorRootBackgroundBrush"] = "#10151D",
        ["EditorPanelBackgroundBrush"] = "#171C24",
        ["EditorPanelBorderBrush"] = "#293241",
        ["EditorPanelStrongBorderBrush"] = "#354255",
        ["EditorCanvasBackgroundBrush"] = "#0D1219",
        ["EditorCanvasGridBackgroundBrush"] = "#111821",
        ["EditorCanvasGridLineBrush"] = "#202936",
        ["EditorNodeBackgroundBrush"] = "#20242A",
        ["EditorExecutionOverlayBrush"] = "#B0181D2A",
        ["EditorExecutionPinBrush"] = "#F4F4F4",
        ["EditorConnectionHitBrush"] = "#2BA8FF",
        ["EditorPreviewConnectionBrush"] = "#F5F5F5",
        ["EditorSectionHeaderBrush"] = "#25303D",
        ["EditorListSelectedBrush"] = "#2D6FB7",
        ["EditorListHoverBrush"] = "#203044",
        ["EditorDirtyBackgroundBrush"] = "#3F2A13",
        ["EditorTextBrush"] = "#D7E0EC",
        ["EditorTextBrightBrush"] = "#F3F7FF",
        ["EditorMutedTextBrush"] = "#9EABBC",
        ["EditorPanelCardBrush"] = "#151B23",
        ["EditorPanelCardHoverBrush"] = "#1C2633",
        ["EditorPanelElevatedBrush"] = "#1B2330",
        ["EditorChromeBrush"] = "#111821",
        ["EditorChromeHighlightBrush"] = "#203044",
        ["EditorToolbarGroupBrush"] = "#151B24",
        ["EditorToolbarSeparatorBrush"] = "#334155",
        ["EditorSectionHeaderAccentBrush"] = "#1E2C3D",
        ["EditorSelectedBorderBrush"] = "#2E7BC5",
        ["EditorFieldCardBrush"] = "#121922",
        ["EditorDisabledChipBrush"] = "#202833",
        ["EditorDisabledChipBorderBrush"] = "#364354",
        ["EditorToolTipBackgroundBrush"] = "#0B1220",
        ["EditorToolTipTextBrush"] = "#F8FAFC",
    };

    private static readonly Dictionary<string, string> LightPalette = new()
    {
        ["AppBackgroundBrush"] = "#DDE3EA",
        ["PanelBackgroundBrush"] = "#E6EBF2",
        ["PanelAltBackgroundBrush"] = "#D0D8E3",
        ["BorderBrushDark"] = "#B4C0CF",
        ["ForegroundPrimaryBrush"] = "#253041",
        ["ForegroundSecondaryBrush"] = "#596575",
        ["AccentForegroundBrush"] = "#FFFFFF",
        ["InputBackgroundBrush"] = "#EEF2F6",
        ["InputBorderBrush"] = "#B2BECD",
        ["DisabledInputBackgroundBrush"] = "#D6DEE8",
        ["DisabledInputBorderBrush"] = "#BBC7D5",
        ["DisabledForegroundBrush"] = "#667085",
        ["DropdownBackgroundBrush"] = "#EEF2F6",
        ["DropdownHoverBrush"] = "#D5E2F4",
        ["DropdownSelectedBrush"] = "#2F6FEB",
        ["DropdownBorderBrush"] = "#B9C5D3",
        ["DropdownTextBrush"] = "#253041",
        ["DropdownMutedTextBrush"] = "#667085",
        ["ToolbarButtonBackgroundBrush"] = "#E6EBF2",
        ["ToolbarButtonBorderBrush"] = "#B4C0CF",
        ["CompileDirtyBackgroundBrush"] = "#FFF7E6",
        ["CompileDirtyBorderBrush"] = "#C98200",
        ["StatusMutedBrush"] = "#475569",
        ["LogErrorBrush"] = "#D92D20",
        ["EditorRootBackgroundBrush"] = "#DDE3EA",
        ["EditorPanelBackgroundBrush"] = "#E6EBF2",
        ["EditorPanelBorderBrush"] = "#B4C0CF",
        ["EditorPanelStrongBorderBrush"] = "#9FAEBF",
        ["EditorCanvasBackgroundBrush"] = "#D6DEE9",
        ["EditorCanvasGridBackgroundBrush"] = "#DCE4EE",
        ["EditorCanvasGridLineBrush"] = "#BFCBDA",
        ["EditorNodeBackgroundBrush"] = "#EEF2F6",
        ["EditorExecutionOverlayBrush"] = "#AADDE5EF",
        ["EditorExecutionPinBrush"] = "#64748B",
        ["EditorConnectionHitBrush"] = "#2F80ED",
        ["EditorPreviewConnectionBrush"] = "#64748B",
        ["EditorSectionHeaderBrush"] = "#D9E1EB",
        ["EditorListSelectedBrush"] = "#2F6FEB",
        ["EditorListHoverBrush"] = "#D5E2F4",
        ["EditorDirtyBackgroundBrush"] = "#FFF7E6",
        ["EditorTextBrush"] = "#253041",
        ["EditorTextBrightBrush"] = "#1F2937",
        ["EditorMutedTextBrush"] = "#667085",
        ["EditorPanelCardBrush"] = "#EEF2F6",
        ["EditorPanelCardHoverBrush"] = "#DEE7F1",
        ["EditorPanelElevatedBrush"] = "#D8E0EA",
        ["EditorChromeBrush"] = "#D0D8E3",
        ["EditorChromeHighlightBrush"] = "#D5E2F4",
        ["EditorToolbarGroupBrush"] = "#E6EBF2",
        ["EditorToolbarSeparatorBrush"] = "#BBC7D5",
        ["EditorSectionHeaderAccentBrush"] = "#D5E2F4",
        ["EditorSelectedBorderBrush"] = "#8BB8FF",
        ["EditorFieldCardBrush"] = "#DCE4EE",
        ["EditorDisabledChipBrush"] = "#D6DEE8",
        ["EditorDisabledChipBorderBrush"] = "#BBC7D5",
        ["EditorToolTipBackgroundBrush"] = "#141820",
        ["EditorToolTipTextBrush"] = "#F8FAFC",
    };

    public static void Apply(AppSettings settings)
    {
        settings.Normalize();
        var palette = settings.ThemeMode == AppThemeMode.Light ? LightPalette : DarkPalette;
        foreach (var (key, value) in palette)
        {
            if (TryParseColor(value, out var color))
                SetBrushColor(key, color);
        }

        var defaultAccent = settings.ThemeMode == AppThemeMode.Light ? "#2F6FEB" : "#4FA3FF";
        if (!TryParseColor(settings.AccentColor, out var accent))
            TryParseColor(defaultAccent, out accent);

        var selection = CreateSelectionColor(accent, settings.ThemeMode);
        var chromeBase = settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xD0, 0xD8, 0xE3)
            : WpfColor.FromRgb(0x11, 0x18, 0x21);
        var cardBase = settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xEE, 0xF2, 0xF6)
            : WpfColor.FromRgb(0x15, 0x1B, 0x23);
        var fieldBase = settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xDC, 0xE4, 0xEE)
            : WpfColor.FromRgb(0x12, 0x19, 0x22);

        SetBrushColor("AccentBrush", accent);
        SetBrushColor("AccentHoverBrush", Lighten(accent, 18));
        SetBrushColor("AccentPressedBrush", Darken(accent, 22));
        SetBrushColor("AccentForegroundBrush", Luminance(accent) > 160
            ? WpfColor.FromRgb(0x1F, 0x29, 0x37)
            : WpfColor.FromRgb(0xFF, 0xFF, 0xFF));
        SetBrushColor("EditorSelectedAccentBrush", accent);
        SetBrushColor("EditorToolTipBorderBrush", accent);
        SetBrushColor("EditorSelectedBorderBrush", settings.ThemeMode == AppThemeMode.Light ? Lighten(selection, 34) : Darken(selection, 12));
        SetBrushColor("EditorListSelectedBrush", selection);
        SetBrushColor("DropdownSelectedBrush", selection);
        SetBrushColor("InputBorderBrush", Blend(fieldBase, accent, 0.38));
        SetBrushColor("DropdownBorderBrush", Blend(fieldBase, accent, 0.28));
        SetBrushColor("EditorPanelCardHoverBrush", Blend(cardBase, accent, settings.ThemeMode == AppThemeMode.Light ? 0.08 : 0.14));
        SetBrushColor("EditorListHoverBrush", Blend(chromeBase, accent, settings.ThemeMode == AppThemeMode.Light ? 0.12 : 0.20));
        SetBrushColor("DropdownHoverBrush", Blend(cardBase, accent, settings.ThemeMode == AppThemeMode.Light ? 0.12 : 0.20));
        SetBrushColor("EditorChromeHighlightBrush", Blend(chromeBase, accent, settings.ThemeMode == AppThemeMode.Light ? 0.16 : 0.22));
        SetBrushColor("EditorSectionHeaderAccentBrush", Blend(chromeBase, accent, settings.ThemeMode == AppThemeMode.Light ? 0.14 : 0.18));
        SetBrushColor("EditorToolbarGroupBrush", Blend(chromeBase, accent, settings.ThemeMode == AppThemeMode.Light ? 0.05 : 0.08));
        SetBrushColor("ToolbarButtonBorderBrush", Blend(chromeBase, accent, settings.ThemeMode == AppThemeMode.Light ? 0.24 : 0.20));
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static bool TryParseColor(string? value, out WpfColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (!text.StartsWith("#", StringComparison.Ordinal))
            text = "#" + text;

        if (text.Length != 7 && text.Length != 9)
            return false;

        try
        {
            if (text.Length == 9)
            {
                var a = byte.Parse(text.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var r = byte.Parse(text.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var g = byte.Parse(text.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var b = byte.Parse(text.Substring(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                color = WpfColor.FromArgb(a, r, g, b);
                return true;
            }

            var rgbR = byte.Parse(text.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var rgbG = byte.Parse(text.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var rgbB = byte.Parse(text.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            color = WpfColor.FromRgb(rgbR, rgbG, rgbB);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string ToHex(WpfColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static void SetBrushColor(string key, WpfColor color)
    {
        var resources = WpfApplication.Current?.Resources;
        if (resources is null)
            return;

        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private static WpfColor Lighten(WpfColor color, byte amount) =>
        WpfColor.FromRgb(Add(color.R, amount), Add(color.G, amount), Add(color.B, amount));

    private static WpfColor Darken(WpfColor color, byte amount) =>
        WpfColor.FromRgb(Subtract(color.R, amount), Subtract(color.G, amount), Subtract(color.B, amount));

    private static WpfColor Blend(WpfColor baseColor, WpfColor tint, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        return WpfColor.FromRgb(
            BlendChannel(baseColor.R, tint.R, ratio),
            BlendChannel(baseColor.G, tint.G, ratio),
            BlendChannel(baseColor.B, tint.B, ratio));
    }

    private static WpfColor CreateSelectionColor(WpfColor accent, AppThemeMode themeMode)
    {
        if (themeMode == AppThemeMode.Dark)
        {
            var darkSelection = accent;
            while (Luminance(darkSelection) > 170)
                darkSelection = Darken(darkSelection, 14);
            return darkSelection;
        }

        // Keep AccentBrush exact. Only selected backgrounds need a readable, slightly deeper version.
        var selection = Blend(accent, WpfColor.FromRgb(0x2B, 0x34, 0x44), 0.30);
        while (Luminance(selection) > 170)
            selection = Blend(selection, WpfColor.FromRgb(0x1F, 0x29, 0x37), 0.18);
        return selection;
    }

    private static double Luminance(WpfColor color) =>
        (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);

    private static byte BlendChannel(byte baseValue, byte tintValue, double ratio) =>
        (byte)Math.Round((baseValue * (1 - ratio)) + (tintValue * ratio));

    private static byte Add(byte value, byte amount) => (byte)Math.Min(255, value + amount);

    private static byte Subtract(byte value, byte amount) => (byte)Math.Max(0, value - amount);
}
