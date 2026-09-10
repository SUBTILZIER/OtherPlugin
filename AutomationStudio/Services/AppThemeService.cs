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
        ["AppBackgroundBrush"] = "#1B1D1F",
        ["PanelBackgroundBrush"] = "#222426",
        ["PanelAltBackgroundBrush"] = "#292C30",
        ["BorderBrushDark"] = "#3A4048",
        ["ForegroundPrimaryBrush"] = "#D8DCE3",
        ["ForegroundSecondaryBrush"] = "#9CA3AD",
        ["AccentForegroundBrush"] = "#FFFFFF",
        ["EditorSelectionTextBrush"] = "#F2F5F8",
        ["InputBackgroundBrush"] = "#272B30",
        ["InputBorderBrush"] = "#454C55",
        ["DisabledInputBackgroundBrush"] = "#202328",
        ["DisabledInputBorderBrush"] = "#353A41",
        ["DisabledForegroundBrush"] = "#686F79",
        ["DropdownBackgroundBrush"] = "#272B30",
        ["DropdownHoverBrush"] = "#32383F",
        ["DropdownSelectedBrush"] = "#294A5B",
        ["DropdownBorderBrush"] = "#454C55",
        ["DropdownTextBrush"] = "#D8DCE3",
        ["DropdownMutedTextBrush"] = "#9CA3AD",
        ["ToolbarButtonBackgroundBrush"] = "#222426",
        ["ToolbarButtonBorderBrush"] = "#3A4048",
        ["CompileDirtyBackgroundBrush"] = "#4A3216",
        ["CompileDirtyBorderBrush"] = "#D88A3D",
        ["StatusMutedBrush"] = "#858C95",
        ["LogErrorBrush"] = "#E06C75",
        ["ValidationErrorBrush"] = "#E06C75",
        ["EditorRootBackgroundBrush"] = "#1B1D1F",
        ["EditorWindowBorderBrush"] = "#3A4048",
        ["EditorPanelBackgroundBrush"] = "#222426",
        ["EditorPanelBorderBrush"] = "#3A4048",
        ["EditorPanelStrongBorderBrush"] = "#4A525C",
        ["EditorCanvasBackgroundBrush"] = "#17191C",
        ["EditorCanvasGridBackgroundBrush"] = "#1B1E22",
        ["EditorCanvasGridLineBrush"] = "#292E34",
        ["EditorNodeBackgroundBrush"] = "#24272B",
        ["EditorExecutionOverlayBrush"] = "#B0181D2A",
        ["EditorExecutionPinBrush"] = "#F4F4F4",
        ["EditorCompletionPinBrush"] = "#FFB848",
        ["EditorFailurePinBrush"] = "#FF5A64",
        ["EditorBooleanPinBrush"] = "#B82D30",
        ["EditorVectorPinBrush"] = "#50C472",
        ["EditorStringPinBrush"] = "#CA2EA5",
        ["EditorDefaultPinBrush"] = "#B8BEC7",
        ["EditorConnectionOutlineBrush"] = "#CC101214",
        ["EditorConnectionHitBrush"] = "#2F9FD0",
        ["EditorPreviewConnectionBrush"] = "#E1E7EA",
        ["EditorSectionHeaderBrush"] = "#292C30",
        ["EditorListSelectedBrush"] = "#294A5B",
        ["EditorListHoverBrush"] = "#32383F",
        ["EditorDirtyBackgroundBrush"] = "#3F2A13",
        ["EditorTextBrush"] = "#D8DCE3",
        ["EditorTextBrightBrush"] = "#E7EAEE",
        ["EditorMutedTextBrush"] = "#9CA3AD",
        ["EditorPanelCardBrush"] = "#292C30",
        ["EditorPanelCardHoverBrush"] = "#32383F",
        ["EditorPanelElevatedBrush"] = "#25282C",
        ["EditorChromeBrush"] = "#1B1E22",
        ["EditorChromeHighlightBrush"] = "#32383F",
        ["EditorToolbarGroupBrush"] = "#222426",
        ["EditorToolbarSeparatorBrush"] = "#3A4048",
        ["EditorSectionHeaderAccentBrush"] = "#263C45",
        ["EditorSelectedBorderBrush"] = "#3E9BB5",
        ["EditorFieldCardBrush"] = "#272B30",
        ["EditorDisabledChipBrush"] = "#202328",
        ["EditorDisabledChipBorderBrush"] = "#353A41",
        ["EditorToolTipBackgroundBrush"] = "#14171A",
        ["EditorToolTipTextBrush"] = "#F2F4F7",
        ["EditorSelectionGlowBrush"] = "#3E9BB5",
        ["EditorSelectionRingBrush"] = "#101214",
        ["EditorSelectionBorderBrush"] = "#3E9BB5",
        ["EditorSelectionFillBrush"] = "#335C97A8",
        ["EditorNodeNumberBackgroundBrush"] = "#66000000",
    };

    private static readonly Dictionary<string, string> LightPalette = new()
    {
        ["AppBackgroundBrush"] = "#F2F3F1",
        ["PanelBackgroundBrush"] = "#F7F8F6",
        ["PanelAltBackgroundBrush"] = "#E9ECEA",
        ["BorderBrushDark"] = "#C7CDCA",
        ["ForegroundPrimaryBrush"] = "#303330",
        ["ForegroundSecondaryBrush"] = "#666B67",
        ["AccentForegroundBrush"] = "#FFFFFF",
        ["EditorSelectionTextBrush"] = "#303330",
        ["InputBackgroundBrush"] = "#FAFBFA",
        ["InputBorderBrush"] = "#BFC7C3",
        ["DisabledInputBackgroundBrush"] = "#E9ECEA",
        ["DisabledInputBorderBrush"] = "#C7CDCA",
        ["DisabledForegroundBrush"] = "#8A908C",
        ["DropdownBackgroundBrush"] = "#F7F8F6",
        ["DropdownHoverBrush"] = "#EEF1EF",
        ["DropdownSelectedBrush"] = "#DCE9F7",
        ["DropdownBorderBrush"] = "#C7CDCA",
        ["DropdownTextBrush"] = "#303330",
        ["DropdownMutedTextBrush"] = "#707671",
        ["ToolbarButtonBackgroundBrush"] = "#E6EAE8",
        ["ToolbarButtonBorderBrush"] = "#C7CDCA",
        ["CompileDirtyBackgroundBrush"] = "#FFF4DC",
        ["CompileDirtyBorderBrush"] = "#B66A00",
        ["StatusMutedBrush"] = "#727672",
        ["LogErrorBrush"] = "#B4232C",
        ["ValidationErrorBrush"] = "#B4232C",
        ["EditorRootBackgroundBrush"] = "#EBEEEC",
        ["EditorWindowBorderBrush"] = "#8B9590",
        ["EditorPanelBackgroundBrush"] = "#F4F6F4",
        ["EditorPanelBorderBrush"] = "#C7CDCA",
        ["EditorPanelStrongBorderBrush"] = "#AEB8B3",
        ["EditorCanvasBackgroundBrush"] = "#D6DEDA",
        ["EditorCanvasGridBackgroundBrush"] = "#D6DEDA",
        ["EditorCanvasGridLineBrush"] = "#B5C1BC",
        ["EditorNodeBackgroundBrush"] = "#F1F3F1",
        ["EditorExecutionOverlayBrush"] = "#A8DDE2E1",
        ["EditorExecutionPinBrush"] = "#34465A",
        ["EditorCompletionPinBrush"] = "#A85A00",
        ["EditorFailurePinBrush"] = "#B4232C",
        ["EditorBooleanPinBrush"] = "#B4232C",
        ["EditorVectorPinBrush"] = "#167647",
        ["EditorStringPinBrush"] = "#A61E76",
        ["EditorDefaultPinBrush"] = "#4B5563",
        ["EditorConnectionOutlineBrush"] = "#CCF4F6F5",
        ["EditorConnectionHitBrush"] = "#276BD1",
        ["EditorPreviewConnectionBrush"] = "#34465A",
        ["EditorSectionHeaderBrush"] = "#E5E9E7",
        ["EditorListSelectedBrush"] = "#DCE9F7",
        ["EditorListHoverBrush"] = "#EEF1EF",
        ["EditorDirtyBackgroundBrush"] = "#FFF4DC",
        ["EditorTextBrush"] = "#3A3D3A",
        ["EditorTextBrightBrush"] = "#303330",
        ["EditorMutedTextBrush"] = "#747A75",
        ["EditorPanelCardBrush"] = "#F7F8F6",
        ["EditorPanelCardHoverBrush"] = "#EEF1EF",
        ["EditorPanelElevatedBrush"] = "#F5F7F5",
        ["EditorChromeBrush"] = "#EBEEEC",
        ["EditorChromeHighlightBrush"] = "#F1F3F1",
        ["EditorToolbarGroupBrush"] = "#E6EAE8",
        ["EditorToolbarSeparatorBrush"] = "#C7CDCA",
        ["EditorSectionHeaderAccentBrush"] = "#E3E8E5",
        ["EditorSelectedBorderBrush"] = "#8FB2DF",
        ["EditorFieldCardBrush"] = "#FAFBFA",
        ["EditorDisabledChipBrush"] = "#E9ECEA",
        ["EditorDisabledChipBorderBrush"] = "#C7CDCA",
        ["EditorToolTipBackgroundBrush"] = "#141820",
        ["EditorToolTipTextBrush"] = "#F8FAFC",
        ["EditorSelectionGlowBrush"] = "#2F6FEB",
        ["EditorSelectionRingBrush"] = "#4B5563",
        ["EditorSelectionBorderBrush"] = "#8FB2DF",
        ["EditorSelectionFillBrush"] = "#335E8FC7",
        ["EditorNodeNumberBackgroundBrush"] = "#66000000",
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

        var defaultAccent = settings.ThemeMode == AppThemeMode.Light ? "#2F6FEB" : "#3E9BB5";
        if (!TryParseColor(settings.AccentColor, out var accent))
            TryParseColor(defaultAccent, out accent);

        var opacity = Math.Clamp(settings.AccentOpacity, 0.18, 1.0);
        var chromeBase = settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xEB, 0xEE, 0xEC)
            : WpfColor.FromRgb(0x1B, 0x1E, 0x22);
        var cardBase = settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xF7, 0xF8, 0xF6)
            : WpfColor.FromRgb(0x29, 0x2C, 0x30);
        var fieldBase = settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xFA, 0xFB, 0xFA)
            : WpfColor.FromRgb(0x27, 0x2B, 0x30);
        var panelBase = settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xF4, 0xF6, 0xF4)
            : WpfColor.FromRgb(0x22, 0x24, 0x26);
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
        SetBrushColor("EditorSelectionGlowBrush", settings.ThemeMode == AppThemeMode.Light
            ? accent
            : accent);
        SetBrushColor("EditorToolTipBorderBrush", accent);
        SetBrushColor("EditorSelectedBorderBrush", settings.ThemeMode == AppThemeMode.Light
            ? Blend(WpfColor.FromRgb(0xD1, 0xD4, 0xD2), accent, 0.54)
            : Darken(selection, 12));
        SetBrushColor("EditorListSelectedBrush", selection);
        SetBrushColor("DropdownSelectedBrush", selection);
        SetBrushColor("InputBorderBrush", settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xBF, 0xC7, 0xC3)
            : Blend(fieldBase, accent, 0.38));
        SetBrushColor("DropdownBorderBrush", settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xC7, 0xCD, 0xCA)
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
            ? WpfColor.FromRgb(0xC7, 0xCD, 0xCA)
            : Blend(chromeBase, accent, 0.20 * opacity));
        SetBrushColor("CompileDirtyBackgroundBrush", settings.ThemeMode == AppThemeMode.Light ? Blend(panelBase, WpfColor.FromRgb(0xF5, 0x9E, 0x0B), 0.08) : WpfColor.FromRgb(0x4A, 0x32, 0x16));
        SetBrushColor("EditorDirtyBackgroundBrush", settings.ThemeMode == AppThemeMode.Light ? Blend(panelBase, WpfColor.FromRgb(0xF5, 0x9E, 0x0B), 0.06) : WpfColor.FromRgb(0x3F, 0x2A, 0x13));
        SetBrushColor("PanelAltBackgroundBrush", settings.ThemeMode == AppThemeMode.Light
            ? WpfColor.FromRgb(0xE6, 0xE8, 0xE7)
            : WpfColor.FromRgb(0x29, 0x2C, 0x30));
        SetColorResource("EditorSelectionGlowColor", settings.ThemeMode == AppThemeMode.Light
            ? accent
            : accent);
        NotifyThemeChanged();
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

    private static void SetColorResource(string key, WpfColor color)
    {
        var resources = WpfApplication.Current?.Resources;
        if (resources is not null)
            resources[key] = color;
    }

    private static void NotifyThemeChanged()
    {
        foreach (EventHandler handler in ThemeChanged?.GetInvocationList().OfType<EventHandler>()
                     ?? Enumerable.Empty<EventHandler>())
        {
            try
            {
                handler(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"主题订阅者刷新失败：{ex.Message}");
            }
        }
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
