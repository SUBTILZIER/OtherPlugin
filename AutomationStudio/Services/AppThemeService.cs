using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;

namespace AutomationStudioWpf.Services;

public static class AppThemeService
{
    private static readonly Dictionary<string, string> DarkPalette = new()
    {
        ["AppBackgroundBrush"] = "#171C24",
        ["PanelBackgroundBrush"] = "#1C232D",
        ["PanelAltBackgroundBrush"] = "#222A35",
        ["BorderBrushDark"] = "#303A48",
        ["ForegroundPrimaryBrush"] = "#E6EDF5",
        ["ForegroundSecondaryBrush"] = "#AAB6C6",
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
        ["AppBackgroundBrush"] = "#F4F1EA",
        ["PanelBackgroundBrush"] = "#FFFCF6",
        ["PanelAltBackgroundBrush"] = "#F0EDE5",
        ["BorderBrushDark"] = "#D8D1C4",
        ["ForegroundPrimaryBrush"] = "#20242A",
        ["ForegroundSecondaryBrush"] = "#5B6470",
        ["InputBackgroundBrush"] = "#FFFFFF",
        ["InputBorderBrush"] = "#CFC7B8",
        ["DisabledInputBackgroundBrush"] = "#ECE8DF",
        ["DisabledInputBorderBrush"] = "#D9D1C2",
        ["DisabledForegroundBrush"] = "#667085",
        ["DropdownBackgroundBrush"] = "#FFFFFF",
        ["DropdownHoverBrush"] = "#EAF2FF",
        ["DropdownSelectedBrush"] = "#2F6FEB",
        ["DropdownBorderBrush"] = "#CDC6BA",
        ["DropdownTextBrush"] = "#20242A",
        ["DropdownMutedTextBrush"] = "#667085",
        ["ToolbarButtonBackgroundBrush"] = "#FFFFFF",
        ["ToolbarButtonBorderBrush"] = "#D8D1C4",
        ["CompileDirtyBackgroundBrush"] = "#FFF3D9",
        ["CompileDirtyBorderBrush"] = "#C98200",
        ["StatusMutedBrush"] = "#64748B",
        ["LogErrorBrush"] = "#D92D20",
        ["EditorRootBackgroundBrush"] = "#F4F1EA",
        ["EditorPanelBackgroundBrush"] = "#FFFCF6",
        ["EditorPanelBorderBrush"] = "#D8D1C4",
        ["EditorPanelStrongBorderBrush"] = "#C5BBAA",
        ["EditorCanvasBackgroundBrush"] = "#FAF8F2",
        ["EditorCanvasGridBackgroundBrush"] = "#FFFEFB",
        ["EditorCanvasGridLineBrush"] = "#E6DED1",
        ["EditorSectionHeaderBrush"] = "#E6E0D6",
        ["EditorListSelectedBrush"] = "#2F6FEB",
        ["EditorListHoverBrush"] = "#EAF2FF",
        ["EditorDirtyBackgroundBrush"] = "#FFF3D9",
        ["EditorTextBrush"] = "#20242A",
        ["EditorTextBrightBrush"] = "#0F172A",
        ["EditorMutedTextBrush"] = "#64748B",
        ["EditorPanelCardBrush"] = "#FFFFFF",
        ["EditorPanelCardHoverBrush"] = "#F2EFE7",
        ["EditorPanelElevatedBrush"] = "#EEE9DF",
        ["EditorChromeBrush"] = "#ECE8DF",
        ["EditorChromeHighlightBrush"] = "#EAF2FF",
        ["EditorToolbarGroupBrush"] = "#FFFFFF",
        ["EditorToolbarSeparatorBrush"] = "#D7D0C3",
        ["EditorSectionHeaderAccentBrush"] = "#EAF2FF",
        ["EditorSelectedBorderBrush"] = "#8BB8FF",
        ["EditorFieldCardBrush"] = "#F5F2EA",
        ["EditorDisabledChipBrush"] = "#ECE8DF",
        ["EditorDisabledChipBorderBrush"] = "#D5CCBD",
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

        var selection = EnsureSelectionColor(accent);
        var chromeBase = settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xEC, 0xE8, 0xDF)
            : WpfColor.FromRgb(0x11, 0x18, 0x21);
        var cardBase = settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xFF, 0xFF, 0xFF)
            : WpfColor.FromRgb(0x15, 0x1B, 0x23);
        var fieldBase = settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xF5, 0xF2, 0xEA)
            : WpfColor.FromRgb(0x12, 0x19, 0x22);

        SetBrushColor("AccentBrush", accent);
        SetBrushColor("AccentHoverBrush", Lighten(accent, 18));
        SetBrushColor("AccentPressedBrush", Darken(accent, 22));
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
    }

    public static bool TryParseColor(string? value, out WpfColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (!text.StartsWith("#", StringComparison.Ordinal))
            text = "#" + text;

        if (text.Length != 7)
            return false;

        try
        {
            var r = byte.Parse(text.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var g = byte.Parse(text.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var b = byte.Parse(text.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            color = WpfColor.FromRgb(r, g, b);
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

    private static WpfColor EnsureSelectionColor(WpfColor accent)
    {
        var selection = accent;
        while (Luminance(selection) > 145)
            selection = Darken(selection, 18);
        return selection;
    }

    private static double Luminance(WpfColor color) =>
        (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);

    private static byte BlendChannel(byte baseValue, byte tintValue, double ratio) =>
        (byte)Math.Round((baseValue * (1 - ratio)) + (tintValue * ratio));

    private static byte Add(byte value, byte amount) => (byte)Math.Min(255, value + amount);

    private static byte Subtract(byte value, byte amount) => (byte)Math.Max(0, value - amount);
}
