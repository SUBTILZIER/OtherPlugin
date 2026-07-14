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
        ["EditorSelectionTextBrush"] = "#F3F7FF",
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
        ["EditorWindowBorderBrush"] = "#303A48",
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
        ["AppBackgroundBrush"] = "#ECEEED",
        ["PanelBackgroundBrush"] = "#F5F6F5",
        ["PanelAltBackgroundBrush"] = "#E6E8E7",
        ["BorderBrushDark"] = "#D1D4D2",
        ["ForegroundPrimaryBrush"] = "#30312F",
        ["ForegroundSecondaryBrush"] = "#686B68",
        ["AccentForegroundBrush"] = "#FFFFFF",
        ["EditorSelectionTextBrush"] = "#30312F",
        ["InputBackgroundBrush"] = "#FAFAF9",
        ["InputBorderBrush"] = "#C9CDCB",
        ["DisabledInputBackgroundBrush"] = "#E9EBEA",
        ["DisabledInputBorderBrush"] = "#D5D8D6",
        ["DisabledForegroundBrush"] = "#858884",
        ["DropdownBackgroundBrush"] = "#F7F8F7",
        ["DropdownHoverBrush"] = "#ECEFEE",
        ["DropdownSelectedBrush"] = "#E3ECF7",
        ["DropdownBorderBrush"] = "#D1D4D2",
        ["DropdownTextBrush"] = "#30312F",
        ["DropdownMutedTextBrush"] = "#777A76",
        ["ToolbarButtonBackgroundBrush"] = "#F1F3F2",
        ["ToolbarButtonBorderBrush"] = "#D4D7D5",
        ["CompileDirtyBackgroundBrush"] = "#FFF4DC",
        ["CompileDirtyBorderBrush"] = "#B66A00",
        ["StatusMutedBrush"] = "#727672",
        ["LogErrorBrush"] = "#C93C37",
        ["EditorRootBackgroundBrush"] = "#E8EAE9",
        ["EditorWindowBorderBrush"] = "#8D9591",
        ["EditorPanelBackgroundBrush"] = "#F3F4F3",
        ["EditorPanelBorderBrush"] = "#D4D7D5",
        ["EditorPanelStrongBorderBrush"] = "#C3C8C5",
        ["EditorCanvasBackgroundBrush"] = "#C5CDCA",
        ["EditorCanvasGridBackgroundBrush"] = "#D0D7D5",
        ["EditorCanvasGridLineBrush"] = "#B5BFBC",
        ["EditorNodeBackgroundBrush"] = "#F6F7F6",
        ["EditorExecutionOverlayBrush"] = "#A8DDE2E1",
        ["EditorExecutionPinBrush"] = "#526171",
        ["EditorConnectionHitBrush"] = "#276BD1",
        ["EditorPreviewConnectionBrush"] = "#526171",
        ["EditorSectionHeaderBrush"] = "#E5E8E6",
        ["EditorListSelectedBrush"] = "#E3ECF7",
        ["EditorListHoverBrush"] = "#ECEFEE",
        ["EditorDirtyBackgroundBrush"] = "#FFF4DC",
        ["EditorTextBrush"] = "#3A3B39",
        ["EditorTextBrightBrush"] = "#242624",
        ["EditorMutedTextBrush"] = "#747874",
        ["EditorPanelCardBrush"] = "#F7F8F7",
        ["EditorPanelCardHoverBrush"] = "#EEF1EF",
        ["EditorPanelElevatedBrush"] = "#F6F7F6",
        ["EditorChromeBrush"] = "#E8EAE9",
        ["EditorChromeHighlightBrush"] = "#EFF2F0",
        ["EditorToolbarGroupBrush"] = "#F0F2F1",
        ["EditorToolbarSeparatorBrush"] = "#D5D8D6",
        ["EditorSectionHeaderAccentBrush"] = "#E8ECEA",
        ["EditorSelectedBorderBrush"] = "#8FB2DF",
        ["EditorFieldCardBrush"] = "#FAFAF9",
        ["EditorDisabledChipBrush"] = "#E9EBEA",
        ["EditorDisabledChipBorderBrush"] = "#D5D8D6",
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

        var opacity = Math.Clamp(settings.AccentOpacity, 0.18, 1.0);
        var chromeBase = settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xE8, 0xEA, 0xE9)
            : WpfColor.FromRgb(0x11, 0x18, 0x21);
        var cardBase = settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xF7, 0xF8, 0xF7)
            : WpfColor.FromRgb(0x15, 0x1B, 0x23);
        var fieldBase = settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xFA, 0xFA, 0xF9)
            : WpfColor.FromRgb(0x12, 0x19, 0x22);
        var panelBase = settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xF3, 0xF4, 0xF3)
            : WpfColor.FromRgb(0x17, 0x1C, 0x24);
        var selection = CreateSelectionColor(accent, settings.ThemeMode, opacity, panelBase);
        var accentChrome = Blend(
            chromeBase,
            accent,
            settings.ThemeMode == AppThemeMode.Light ? 0.04 + (0.04 * opacity) : 0.28 * opacity);

        SetBrushColor("AccentBrush", accent);
        SetBrushColor("AccentHoverBrush", Lighten(accent, 18));
        SetBrushColor("AccentPressedBrush", Darken(accent, 22));
        SetBrushColor("AccentForegroundBrush", Luminance(accent) > 155
            ? WpfColor.FromRgb(0x1F, 0x29, 0x37)
            : WpfColor.FromRgb(0xFF, 0xFF, 0xFF));
        SetBrushColor("EditorSelectionTextBrush", settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0x30, 0x31, 0x2F)
            : WpfColor.FromRgb(0xF3, 0xF7, 0xFF));
        SetBrushColor("EditorSelectedAccentBrush", settings.ThemeMode == AppThemeMode.Light
            ? Blend(accent, panelBase, 0.08)
            : Blend(panelBase, accent, 0.72));
        SetBrushColor("EditorToolTipBorderBrush", accent);
        SetBrushColor("EditorSelectedBorderBrush", settings.ThemeMode == AppThemeMode.Light
            ? Blend(WpfColor.FromRgb(0xD1, 0xD4, 0xD2), accent, 0.54)
            : Darken(selection, 12));
        SetBrushColor("EditorListSelectedBrush", selection);
        SetBrushColor("DropdownSelectedBrush", selection);
        SetBrushColor("InputBorderBrush", settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xC9, 0xCD, 0xCB)
            : Blend(fieldBase, accent, 0.38));
        SetBrushColor("DropdownBorderBrush", settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xD1, 0xD4, 0xD2)
            : Blend(fieldBase, accent, 0.28));
        SetBrushColor("EditorPanelCardHoverBrush", Blend(
            cardBase,
            accent,
            settings.ThemeMode == AppThemeMode.Light ? 0.025 + (0.035 * opacity) : 0.14 * opacity));
        SetBrushColor("EditorListHoverBrush", Blend(
            chromeBase,
            accent,
            settings.ThemeMode == AppThemeMode.Light ? 0.035 + (0.04 * opacity) : 0.20 * opacity));
        SetBrushColor("DropdownHoverBrush", Blend(
            cardBase,
            accent,
            settings.ThemeMode == AppThemeMode.Light ? 0.035 + (0.04 * opacity) : 0.20 * opacity));
        SetBrushColor("EditorChromeHighlightBrush", accentChrome);
        SetBrushColor("EditorSectionHeaderAccentBrush", accentChrome);
        SetBrushColor("EditorToolbarGroupBrush", settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xF0, 0xF2, 0xF1)
            : Blend(chromeBase, accent, 0.08 * opacity));
        SetBrushColor("ToolbarButtonBorderBrush", settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xD4, 0xD7, 0xD5)
            : Blend(chromeBase, accent, 0.20 * opacity));
        SetBrushColor("CompileDirtyBackgroundBrush", settings.ThemeMode == AppThemeMode.Light ? Blend(panelBase, WpfColor.FromRgb(0xF5, 0x9E, 0x0B), 0.08) : WpfColor.FromRgb(0x4A, 0x32, 0x16));
        SetBrushColor("EditorDirtyBackgroundBrush", settings.ThemeMode == AppThemeMode.Light ? Blend(panelBase, WpfColor.FromRgb(0xF5, 0x9E, 0x0B), 0.06) : WpfColor.FromRgb(0x3F, 0x2A, 0x13));
        SetBrushColor("PanelAltBackgroundBrush", settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xE6, 0xE8, 0xE7)
            : WpfColor.FromRgb(0x22, 0x2A, 0x35));
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

    private static WpfColor CreateSelectionColor(WpfColor accent, AppThemeMode themeMode, double opacity, WpfColor baseColor)
    {
        if (themeMode == AppThemeMode.Dark)
        {
            var darkSelection = Blend(baseColor, accent, 0.34 + (0.28 * opacity));
            while (Luminance(darkSelection) > 170)
                darkSelection = Darken(darkSelection, 14);
            return darkSelection;
        }

        return Blend(baseColor, accent, 0.07 + (0.10 * opacity));
    }

    private static double Luminance(WpfColor color) =>
        (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);

    private static byte BlendChannel(byte baseValue, byte tintValue, double ratio) =>
        (byte)Math.Round((baseValue * (1 - ratio)) + (tintValue * ratio));

    private static byte Add(byte value, byte amount) => (byte)Math.Min(255, value + amount);

    private static byte Subtract(byte value, byte amount) => (byte)Math.Max(0, value - amount);
}
