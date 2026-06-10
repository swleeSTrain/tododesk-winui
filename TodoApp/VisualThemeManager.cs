using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;

namespace TodoApp;

internal enum AppVisualTheme
{
    Liquid,
    WindowsXp,
    Aero,
    Fluent
}

internal enum AppWindowTreatment
{
    Opaque,
    LiquidGlass
}

internal sealed record ThemeDefinition(
    AppVisualTheme Theme,
    string Tag,
    string DisplayName,
    AppWindowTreatment WindowTreatment,
    ThemePalette LightPalette,
    ThemePalette DarkPalette,
    string[] Aliases)
{
    public bool UsesAcrylicBackdrop => WindowTreatment == AppWindowTreatment.LiquidGlass;

    public bool UsesCustomTitleBar => WindowTreatment == AppWindowTreatment.LiquidGlass;

    public bool UsesLiquidBackdrop => WindowTreatment == AppWindowTreatment.LiquidGlass;

    public byte WindowAlpha => WindowTreatment == AppWindowTreatment.LiquidGlass ? (byte)248 : (byte)255;
}

internal sealed record ThemePalette(
    IReadOnlyDictionary<string, string> Solids,
    IReadOnlyDictionary<string, string[]> Gradients);

internal static class VisualThemeManager
{
    private const string SettingsKey = "visual_theme";
    private const AppVisualTheme DefaultTheme = AppVisualTheme.Fluent;

    private static readonly ThemeDefinition[] Definitions = CreateDefinitions();

    public static event Action<AppVisualTheme>? ThemeApplied;

    public static AppVisualTheme CurrentTheme { get; private set; } = DefaultTheme;

    public static ThemeDefinition CurrentDefinition => GetDefinition(CurrentTheme);

    public static AppVisualTheme LoadSavedTheme()
    {
        var theme = DefaultTheme;

        try
        {
            if (ApplicationData.Current.LocalSettings.Values[SettingsKey] is string value
                && TryParseTag(value, out var savedTheme))
            {
                theme = savedTheme;
            }
        }
        catch
        {
        }

        CurrentTheme = GetDefinition(theme).Theme;
        return CurrentTheme;
    }

    public static void SaveTheme(AppVisualTheme theme)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[SettingsKey] = ToTag(theme);
        }
        catch
        {
        }
    }

    public static string ToTag(AppVisualTheme theme) => GetDefinition(theme).Tag;

    public static string GetDisplayName(AppVisualTheme theme) => GetDefinition(theme).DisplayName;

    public static ThemeDefinition GetDefinition(AppVisualTheme theme)
    {
        foreach (var definition in Definitions)
        {
            if (definition.Theme == theme)
            {
                return definition;
            }
        }

        return GetDefaultDefinition();
    }

    public static bool TryParseTag(string? tag, out AppVisualTheme theme)
    {
        var normalized = tag?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            theme = DefaultTheme;
            return false;
        }

        foreach (var definition in Definitions)
        {
            if (definition.Tag.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                theme = definition.Theme;
                return true;
            }

            foreach (var alias in definition.Aliases)
            {
                if (alias.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    theme = definition.Theme;
                    return true;
                }
            }
        }

        theme = DefaultTheme;
        return false;
    }

    public static void Apply(AppVisualTheme theme)
    {
        var definition = GetDefinition(theme);
        CurrentTheme = definition.Theme;

        if (Application.Current?.Resources is ResourceDictionary resources)
        {
            ApplyThemeDictionary(resources, "Light", definition.LightPalette);
            ApplyThemeDictionary(resources, "Dark", definition.DarkPalette);
        }

        ThemeApplied?.Invoke(definition.Theme);
    }

    private static ThemeDefinition GetDefaultDefinition()
    {
        foreach (var definition in Definitions)
        {
            if (definition.Theme == DefaultTheme)
            {
                return definition;
            }
        }

        return Definitions[0];
    }

    private static void ApplyThemeDictionary(ResourceDictionary resources, string key, ThemePalette palette)
    {
        if (!resources.ThemeDictionaries.TryGetValue(key, out var dictionaryObject)
            || dictionaryObject is not ResourceDictionary dictionary)
        {
            return;
        }

        foreach (var entry in palette.Gradients)
        {
            SetGradient(dictionary, entry.Key, entry.Value);
        }

        foreach (var entry in palette.Solids)
        {
            SetSolid(dictionary, entry.Key, entry.Value);
        }
    }

    private static ThemeDefinition[] CreateDefinitions()
    {
        return new[]
        {
            new ThemeDefinition(
                AppVisualTheme.Fluent,
                "fluent",
                "WinUI Fluent",
                AppWindowTreatment.Opaque,
                CreateFluentPalette(dark: false),
                CreateFluentPalette(dark: true),
                new[] { "winui", "default", "basic" }),
            new ThemeDefinition(
                AppVisualTheme.Liquid,
                "liquid",
                "Liquid Glass",
                AppWindowTreatment.LiquidGlass,
                CreateLiquidLightPalette(),
                CreateLiquidDarkPalette(),
                new[] { "liquidglass", "liquid-glass" }),
            new ThemeDefinition(
                AppVisualTheme.WindowsXp,
                "xp",
                "Windows XP",
                AppWindowTreatment.LiquidGlass,
                CreateWindowsXpLightPalette(),
                CreateWindowsXpDarkPalette(),
                new[] { "windowsxp", "windows-xp" }),
            new ThemeDefinition(
                AppVisualTheme.Aero,
                "aero",
                "Aero",
                AppWindowTreatment.LiquidGlass,
                CreateAeroLightPalette(),
                CreateAeroDarkPalette(),
                new[] { "vista", "win7" })
        };
    }

    private static ThemePalette CreateLiquidDarkPalette()
    {
        return CreatePalette(
            new[]
            {
                ("ContentMaterialStrokeBrush", "#66FFFFFF"),
                ("LiquidGlassSurfaceBrush", "#34303947"),
                ("LiquidGlassToolbarBrush", "#2A242C3A"),
                ("LiquidGlassRowBrush", "#24313A48"),
                ("LiquidGlassControlCheckedBrush", "#40314361"),
                ("LiquidGlassHighlightBrush", "#2AFFFFFF"),
                ("LiquidGlassHoverBrush", "#22FFFFFF"),
                ("LiquidGlassSelectedBrush", "#553F8CFF"),
                ("LiquidGlassStrokeBrush", "#5AFFFFFF"),
                ("LiquidGlassShadowStrokeBrush", "#66000000"),
                ("LiquidGlassCastShadowBrush", "#52000000"),
                ("LiquidGlassTextPrimaryBrush", "#FFFFFFFF"),
                ("LiquidGlassTextSecondaryBrush", "#D8FFFFFF"),
                ("LiquidGlassAccentBrush", "#FF64D2FF"),
                ("LiquidGlassAccentSoftBrush", "#355AC8FA"),
                ("LiquidGlassChromaticWarmStrokeBrush", "#56FF7AD9"),
                ("LiquidGlassChromaticCoolStrokeBrush", "#525AC8FA"),
                ("LiquidGlassWarningBrush", "#FFFFD60A"),
                ("LiquidGlassReviewBrush", "#FFD0A7FF")
            },
            new[]
            {
                Gradient("LiquidGlassWindowBrush", "#680D1624", "#5C241C33", "#620E2E27"),
                Gradient("LiquidGlassRegularWashBrush", "#30000000", "#26151B28", "#3210181F"),
                Gradient("LiquidGlassContentBackdropWashBrush", "#34000000", "#24141B25", "#30101A1F"),
                Gradient("ContentMaterialSurfaceBrush", "#58263041", "#4428323F", "#52313D4A"),
                Gradient("ContentMaterialRowBrush", "#3E2A3444", "#3028313D", "#38323D48"),
                Gradient("LiquidGlassSidebarBrush", "#48202532", "#36303A49", "#4423303A"),
                Gradient("LiquidGlassDropDownBrush", "#9F2B3342", "#8A242B3A", "#94263744"),
                Gradient("LiquidGlassContentInfusionBrush", "#00000000", "#1C3F8CFF", "#18FFD60A", "#1CD0A7FF", "#1830D158"),
                Gradient("LiquidGlassCoolContentBrush", "#0064D2FF", "#2A5AC8FA", "#1C6CFFDE", "#003F8CFF"),
                Gradient("LiquidGlassWarmContentBrush", "#00FFD60A", "#1CFFD60A", "#20FF7AD9", "#00D0A7FF"),
                Gradient("LiquidGlassRefractionBrush", "#00000000", "#2864D2FF", "#18FFFFFF", "#24FF7AD9", "#00000000"),
                Gradient("LiquidGlassSecondaryRefractionBrush", "#00000000", "#1CFFD6E8", "#20FFFFFF", "#1C6CFFDE", "#00000000"),
                Gradient("LiquidGlassSpecularBrush", "#76FFFFFF", "#1AFFFFFF", "#04FFFFFF"),
                Gradient("LiquidGlassEdgeGlowBrush", "#B0FFFFFF", "#10FFFFFF", "#345AC8FA"),
                Gradient("LiquidGlassRimBrush", "#A6FFFFFF", "#1EFFFFFF", "#245AC8FA", "#58FFFFFF"),
                Gradient("LiquidGlassCausticBrush", "#00000000", "#24FFFFFF", "#205AC8FA", "#22FF7AD9", "#00000000"),
                Gradient("LiquidGlassDistortionBrush", "#00000000", "#18FF7AD9", "#2AFFFFFF", "#205AC8FA", "#00000000", "#18FFFFFF", "#00000000")
            });
    }

    private static ThemePalette CreateLiquidLightPalette()
    {
        return CreatePalette(
            new[]
            {
                ("ContentMaterialStrokeBrush", "#B4FFFFFF"),
                ("LiquidGlassSurfaceBrush", "#34FFFFFF"),
                ("LiquidGlassToolbarBrush", "#22FFFFFF"),
                ("LiquidGlassRowBrush", "#1CFFFFFF"),
                ("LiquidGlassControlCheckedBrush", "#34EAF8FF"),
                ("LiquidGlassHighlightBrush", "#A8FFFFFF"),
                ("LiquidGlassHoverBrush", "#28FFFFFF"),
                ("LiquidGlassSelectedBrush", "#321D9BFF"),
                ("LiquidGlassStrokeBrush", "#AAFFFFFF"),
                ("LiquidGlassShadowStrokeBrush", "#34064A7A"),
                ("LiquidGlassCastShadowBrush", "#2E002A40"),
                ("LiquidGlassTextPrimaryBrush", "#FF001526"),
                ("LiquidGlassTextSecondaryBrush", "#DC001526"),
                ("LiquidGlassAccentBrush", "#FF0A84FF"),
                ("LiquidGlassAccentSoftBrush", "#2E62D9FF"),
                ("LiquidGlassChromaticWarmStrokeBrush", "#62FF5FD2"),
                ("LiquidGlassChromaticCoolStrokeBrush", "#5C5AC8FA"),
                ("LiquidGlassWarningBrush", "#FFFFB340"),
                ("LiquidGlassReviewBrush", "#FFBF5AF2")
            },
            new[]
            {
                Gradient("LiquidGlassWindowBrush", "#3CDDEFFF", "#30FFF0FB", "#32EAFBF1"),
                Gradient("LiquidGlassRegularWashBrush", "#24FFFFFF", "#18F3FBFF", "#20FFFFFF"),
                Gradient("LiquidGlassContentBackdropWashBrush", "#22FFFFFF", "#12EDF8FF", "#1CFFFFFF"),
                Gradient("ContentMaterialSurfaceBrush", "#5CFFFFFF", "#40F4FAFF", "#52FFFFFF"),
                Gradient("ContentMaterialRowBrush", "#40FFFFFF", "#2CF5FBFF", "#36FFFFFF"),
                Gradient("LiquidGlassSidebarBrush", "#4EFFFFFF", "#34F2FBFF", "#44FFFFFF"),
                Gradient("LiquidGlassDropDownBrush", "#BCFFFFFF", "#9EEAFBFF", "#A8E4FFF8"),
                Gradient("LiquidGlassContentInfusionBrush", "#00FFFFFF", "#160A84FF", "#14FFB340", "#16BF5AF2", "#1430D158"),
                Gradient("LiquidGlassCoolContentBrush", "#0064D2FF", "#2C5AC8FA", "#1C6CFFDE", "#000A84FF"),
                Gradient("LiquidGlassWarmContentBrush", "#00FFB340", "#20FFB340", "#1CFF5FD2", "#00BF5AF2"),
                Gradient("LiquidGlassRefractionBrush", "#00FFFFFF", "#4FFFFFFF", "#2264D2FF", "#1CFF7AD9", "#00FFFFFF"),
                Gradient("LiquidGlassSecondaryRefractionBrush", "#00FFFFFF", "#24FFD6E8", "#36FFFFFF", "#206CFFDE", "#00FFFFFF"),
                Gradient("LiquidGlassSpecularBrush", "#D4FFFFFF", "#30FFFFFF", "#08FFFFFF"),
                Gradient("LiquidGlassEdgeGlowBrush", "#FFFFFFFF", "#12FFFFFF", "#3E0A84FF"),
                Gradient("LiquidGlassRimBrush", "#FFFFFFFF", "#48FFFFFF", "#1A0A84FF", "#72FFFFFF"),
                Gradient("LiquidGlassCausticBrush", "#00FFFFFF", "#42FFFFFF", "#1D5AC8FA", "#26FF7AD9", "#00FFFFFF"),
                Gradient("LiquidGlassDistortionBrush", "#00FFFFFF", "#16FF4FD8", "#38FFFFFF", "#1E64D2FF", "#00FFFFFF", "#20FFFFFF", "#00FFFFFF")
            });
    }

    private static ThemePalette CreateWindowsXpDarkPalette()
    {
        return CreatePalette(
            new[]
            {
                ("ContentMaterialStrokeBrush", "#B8A7D4FF"),
                ("LiquidGlassSurfaceBrush", "#8C1A4E8A"),
                ("LiquidGlassToolbarBrush", "#76205EA7"),
                ("LiquidGlassRowBrush", "#5C153A72"),
                ("LiquidGlassControlCheckedBrush", "#7C4E8F23"),
                ("LiquidGlassHighlightBrush", "#80D8EDFF"),
                ("LiquidGlassHoverBrush", "#4A7DBBFF"),
                ("LiquidGlassSelectedBrush", "#9A79B63A"),
                ("LiquidGlassStrokeBrush", "#B5C8E8FF"),
                ("LiquidGlassShadowStrokeBrush", "#AA031338"),
                ("LiquidGlassCastShadowBrush", "#8A000E2F"),
                ("LiquidGlassTextPrimaryBrush", "#FFFFFFFF"),
                ("LiquidGlassTextSecondaryBrush", "#E8E9F4FF"),
                ("LiquidGlassAccentBrush", "#FF77B638"),
                ("LiquidGlassAccentSoftBrush", "#6677B638"),
                ("LiquidGlassChromaticWarmStrokeBrush", "#9AF6C45C"),
                ("LiquidGlassChromaticCoolStrokeBrush", "#A06EB6FF"),
                ("LiquidGlassWarningBrush", "#FFFFC54C"),
                ("LiquidGlassReviewBrush", "#FFC18BFF")
            },
            new[]
            {
                Gradient("LiquidGlassWindowBrush", "#F2143768", "#F01F4F8E", "#F00A2C54"),
                Gradient("LiquidGlassRegularWashBrush", "#5A0A2E62", "#3E1D5A99", "#4E09234D"),
                Gradient("LiquidGlassContentBackdropWashBrush", "#52092652", "#382D73B8", "#460B315E"),
                Gradient("ContentMaterialSurfaceBrush", "#B4183765", "#9E275E9B", "#AC102D58"),
                Gradient("ContentMaterialRowBrush", "#8A173A6C", "#74285D98", "#7C0F305E"),
                Gradient("LiquidGlassSidebarBrush", "#C0143A70", "#A4265E98", "#B00D315E"),
                Gradient("LiquidGlassDropDownBrush", "#E91C4F8A", "#DA2F74B7", "#E014386A"),
                Gradient("LiquidGlassContentInfusionBrush", "#00143B72", "#354395D6", "#4077B638", "#2056A7E8", "#221C4F8A"),
                Gradient("LiquidGlassCoolContentBrush", "#00386FCE", "#4869AEFF", "#294DD0FF", "#001B56A0"),
                Gradient("LiquidGlassWarmContentBrush", "#0077B638", "#3A77B638", "#24F6C45C", "#000B315E"),
                Gradient("LiquidGlassRefractionBrush", "#000B315E", "#46CDE8FF", "#2A6EB6FF", "#3277B638", "#000B315E"),
                Gradient("LiquidGlassSecondaryRefractionBrush", "#000B315E", "#307DBBFF", "#36FFFFFF", "#2477B638", "#000B315E"),
                Gradient("LiquidGlassSpecularBrush", "#A8F5FBFF", "#2CEAF7FF", "#06FFFFFF"),
                Gradient("LiquidGlassEdgeGlowBrush", "#F2F8FDFF", "#284A8FE8", "#6B77B638"),
                Gradient("LiquidGlassRimBrush", "#F4FFFFFF", "#647DBBFF", "#4A77B638", "#95D8EDFF"),
                Gradient("LiquidGlassCausticBrush", "#00000000", "#46CDE8FF", "#2F5BA3E6", "#4077B638", "#00000000"),
                Gradient("LiquidGlassDistortionBrush", "#00000000", "#2677B638", "#46FFFFFF", "#3469AEFF", "#00000000", "#20F6C45C", "#00000000")
            });
    }

    private static ThemePalette CreateWindowsXpLightPalette()
    {
        return CreatePalette(
            new[]
            {
                ("ContentMaterialStrokeBrush", "#FF7EADE3"),
                ("LiquidGlassSurfaceBrush", "#BFEAF6FF"),
                ("LiquidGlassToolbarBrush", "#A8D7ECFF"),
                ("LiquidGlassRowBrush", "#86FFFFFF"),
                ("LiquidGlassControlCheckedBrush", "#BB77B638"),
                ("LiquidGlassHighlightBrush", "#E8FFFFFF"),
                ("LiquidGlassHoverBrush", "#629FDBFF"),
                ("LiquidGlassSelectedBrush", "#9677B638"),
                ("LiquidGlassStrokeBrush", "#F0FFFFFF"),
                ("LiquidGlassShadowStrokeBrush", "#7A0B3F85"),
                ("LiquidGlassCastShadowBrush", "#5402397A"),
                ("LiquidGlassTextPrimaryBrush", "#FF061C48"),
                ("LiquidGlassTextSecondaryBrush", "#D8061C48"),
                ("LiquidGlassAccentBrush", "#FF3C8D0D"),
                ("LiquidGlassAccentSoftBrush", "#5677B638"),
                ("LiquidGlassChromaticWarmStrokeBrush", "#88F6C45C"),
                ("LiquidGlassChromaticCoolStrokeBrush", "#8A539DFF"),
                ("LiquidGlassWarningBrush", "#FFFFB000"),
                ("LiquidGlassReviewBrush", "#FF8E5BD6")
            },
            new[]
            {
                Gradient("LiquidGlassWindowBrush", "#F1539DFF", "#F17DBBFF", "#F0A8D8FF"),
                Gradient("LiquidGlassRegularWashBrush", "#70EAF5FF", "#55CFE9FF", "#68F7FBFF"),
                Gradient("LiquidGlassContentBackdropWashBrush", "#72E4F2FF", "#55F8FBFF", "#62CFE8FF"),
                Gradient("ContentMaterialSurfaceBrush", "#DDF7FBFF", "#CBE6F4FF", "#D8FFFFFF"),
                Gradient("ContentMaterialRowBrush", "#C8FFFFFF", "#AEE8F5FF", "#BDF9FEFF"),
                Gradient("LiquidGlassSidebarBrush", "#E7DDEEFF", "#D2BBD9FF", "#E7F8FBFF"),
                Gradient("LiquidGlassDropDownBrush", "#F6F5FBFF", "#E8DDEEFF", "#F2C6E8FF"),
                Gradient("LiquidGlassContentInfusionBrush", "#00FFFFFF", "#30539DFF", "#3477B638", "#2A9FDBFF", "#20FFFFFF"),
                Gradient("LiquidGlassCoolContentBrush", "#00539DFF", "#5C69AEFF", "#3C9FDBFF", "#00539DFF"),
                Gradient("LiquidGlassWarmContentBrush", "#0077B638", "#4A77B638", "#35F6C45C", "#009FDBFF"),
                Gradient("LiquidGlassRefractionBrush", "#00FFFFFF", "#73FFFFFF", "#3869AEFF", "#4077B638", "#00FFFFFF"),
                Gradient("LiquidGlassSecondaryRefractionBrush", "#00FFFFFF", "#449FDBFF", "#66FFFFFF", "#3977B638", "#00FFFFFF"),
                Gradient("LiquidGlassSpecularBrush", "#F2FFFFFF", "#5CFFFFFF", "#0FFFFFFF"),
                Gradient("LiquidGlassEdgeGlowBrush", "#FFFFFFFF", "#2EFFFFFF", "#A677B638"),
                Gradient("LiquidGlassRimBrush", "#FFFFFFFF", "#8CFFFFFF", "#6277B638", "#D6FFFFFF"),
                Gradient("LiquidGlassCausticBrush", "#00FFFFFF", "#68FFFFFF", "#3569AEFF", "#5277B638", "#00FFFFFF"),
                Gradient("LiquidGlassDistortionBrush", "#00FFFFFF", "#2C77B638", "#60FFFFFF", "#3F69AEFF", "#00FFFFFF", "#38F6C45C", "#00FFFFFF")
            });
    }

    private static ThemePalette CreateAeroDarkPalette()
    {
        return CreatePalette(
            new[]
            {
                ("ContentMaterialStrokeBrush", "#72CDEEFF"),
                ("LiquidGlassSurfaceBrush", "#442F4656"),
                ("LiquidGlassToolbarBrush", "#38283E4D"),
                ("LiquidGlassRowBrush", "#2E2D4352"),
                ("LiquidGlassControlCheckedBrush", "#4E2B9EFF"),
                ("LiquidGlassHighlightBrush", "#3CFFFFFF"),
                ("LiquidGlassHoverBrush", "#2689D8FF"),
                ("LiquidGlassSelectedBrush", "#5A2B9EFF"),
                ("LiquidGlassStrokeBrush", "#6DDCF7FF"),
                ("LiquidGlassShadowStrokeBrush", "#76000000"),
                ("LiquidGlassCastShadowBrush", "#64000000"),
                ("LiquidGlassTextPrimaryBrush", "#FFFFFFFF"),
                ("LiquidGlassTextSecondaryBrush", "#D9EAF8FF"),
                ("LiquidGlassAccentBrush", "#FF6CD7FF"),
                ("LiquidGlassAccentSoftBrush", "#3B7CD7FF"),
                ("LiquidGlassChromaticWarmStrokeBrush", "#62FF7AD9"),
                ("LiquidGlassChromaticCoolStrokeBrush", "#727AD7FF"),
                ("LiquidGlassWarningBrush", "#FFFFD166"),
                ("LiquidGlassReviewBrush", "#FFB18CFF")
            },
            new[]
            {
                Gradient("LiquidGlassWindowBrush", "#E0111A24", "#DC1A2734", "#E00B1118"),
                Gradient("LiquidGlassRegularWashBrush", "#46141D28", "#301E3546", "#3A0C1218"),
                Gradient("LiquidGlassContentBackdropWashBrush", "#4018212C", "#2A1F3949", "#340C131A"),
                Gradient("ContentMaterialSurfaceBrush", "#6E1D2B38", "#58233D4F", "#6221323D"),
                Gradient("ContentMaterialRowBrush", "#52233240", "#402D4658", "#48223542"),
                Gradient("LiquidGlassSidebarBrush", "#6C18232F", "#50263D4E", "#5A101821"),
                Gradient("LiquidGlassDropDownBrush", "#B51B2935", "#9E274457", "#AA172631"),
                Gradient("LiquidGlassContentInfusionBrush", "#00000000", "#2038BDF8", "#187AD7FF", "#24C271FF", "#10000000"),
                Gradient("LiquidGlassCoolContentBrush", "#006CD7FF", "#327AD7FF", "#2488FFF0", "#006CD7FF"),
                Gradient("LiquidGlassWarmContentBrush", "#00FFFFFF", "#1EFFFFFF", "#1EFF7AD9", "#00000000"),
                Gradient("LiquidGlassRefractionBrush", "#00000000", "#347AD7FF", "#20FFFFFF", "#2472FFF0", "#00000000"),
                Gradient("LiquidGlassSecondaryRefractionBrush", "#00000000", "#22FFFFFF", "#2C7AD7FF", "#1E88FFF0", "#00000000"),
                Gradient("LiquidGlassSpecularBrush", "#88FFFFFF", "#2CFFFFFF", "#05FFFFFF"),
                Gradient("LiquidGlassEdgeGlowBrush", "#C8FFFFFF", "#16FFFFFF", "#487AD7FF"),
                Gradient("LiquidGlassRimBrush", "#D8FFFFFF", "#28FFFFFF", "#367AD7FF", "#70FFFFFF"),
                Gradient("LiquidGlassCausticBrush", "#00000000", "#28FFFFFF", "#2A7AD7FF", "#2688FFF0", "#00000000"),
                Gradient("LiquidGlassDistortionBrush", "#00000000", "#1A88FFF0", "#30FFFFFF", "#247AD7FF", "#00000000", "#18FFFFFF", "#00000000")
            });
    }

    private static ThemePalette CreateAeroLightPalette()
    {
        return CreatePalette(
            new[]
            {
                ("ContentMaterialStrokeBrush", "#B8FFFFFF"),
                ("LiquidGlassSurfaceBrush", "#3CFFFFFF"),
                ("LiquidGlassToolbarBrush", "#30FFFFFF"),
                ("LiquidGlassRowBrush", "#26FFFFFF"),
                ("LiquidGlassControlCheckedBrush", "#3E7AD7FF"),
                ("LiquidGlassHighlightBrush", "#C8FFFFFF"),
                ("LiquidGlassHoverBrush", "#3889D8FF"),
                ("LiquidGlassSelectedBrush", "#427AD7FF"),
                ("LiquidGlassStrokeBrush", "#B8FFFFFF"),
                ("LiquidGlassShadowStrokeBrush", "#3600446A"),
                ("LiquidGlassCastShadowBrush", "#3200243A"),
                ("LiquidGlassTextPrimaryBrush", "#FF061824"),
                ("LiquidGlassTextSecondaryBrush", "#DD061824"),
                ("LiquidGlassAccentBrush", "#FF0078D7"),
                ("LiquidGlassAccentSoftBrush", "#387AD7FF"),
                ("LiquidGlassChromaticWarmStrokeBrush", "#70FF7AD9"),
                ("LiquidGlassChromaticCoolStrokeBrush", "#707AD7FF"),
                ("LiquidGlassWarningBrush", "#FFFFB900"),
                ("LiquidGlassReviewBrush", "#FF8764B8")
            },
            new[]
            {
                Gradient("LiquidGlassWindowBrush", "#98DFF8FF", "#78C9EFFF", "#8AF8FDFF"),
                Gradient("LiquidGlassRegularWashBrush", "#38FFFFFF", "#24D8F2FF", "#2FFFFFFF"),
                Gradient("LiquidGlassContentBackdropWashBrush", "#36FFFFFF", "#20D8F4FF", "#2AFFFFFF"),
                Gradient("ContentMaterialSurfaceBrush", "#76FFFFFF", "#58DDF5FF", "#68FFFFFF"),
                Gradient("ContentMaterialRowBrush", "#54FFFFFF", "#3BDDF5FF", "#48FFFFFF"),
                Gradient("LiquidGlassSidebarBrush", "#70FFFFFF", "#50D1F1FF", "#60FFFFFF"),
                Gradient("LiquidGlassDropDownBrush", "#CFFFFFFF", "#A8DDF5FF", "#B8FFFFFF"),
                Gradient("LiquidGlassContentInfusionBrush", "#00FFFFFF", "#1D0078D7", "#18C271FF", "#1BFF7AD9", "#12FFFFFF"),
                Gradient("LiquidGlassCoolContentBrush", "#000078D7", "#337AD7FF", "#2288FFF0", "#000078D7"),
                Gradient("LiquidGlassWarmContentBrush", "#00FFFFFF", "#24FFFFFF", "#1EFF7AD9", "#000078D7"),
                Gradient("LiquidGlassRefractionBrush", "#00FFFFFF", "#58FFFFFF", "#267AD7FF", "#2288FFF0", "#00FFFFFF"),
                Gradient("LiquidGlassSecondaryRefractionBrush", "#00FFFFFF", "#30FFFFFF", "#3A7AD7FF", "#2088FFF0", "#00FFFFFF"),
                Gradient("LiquidGlassSpecularBrush", "#E8FFFFFF", "#46FFFFFF", "#0AFFFFFF"),
                Gradient("LiquidGlassEdgeGlowBrush", "#FFFFFFFF", "#18FFFFFF", "#527AD7FF"),
                Gradient("LiquidGlassRimBrush", "#FFFFFFFF", "#58FFFFFF", "#267AD7FF", "#90FFFFFF"),
                Gradient("LiquidGlassCausticBrush", "#00FFFFFF", "#50FFFFFF", "#247AD7FF", "#2E88FFF0", "#00FFFFFF"),
                Gradient("LiquidGlassDistortionBrush", "#00FFFFFF", "#1E88FFF0", "#4FFFFFFF", "#2A7AD7FF", "#00FFFFFF", "#24FFFFFF", "#00FFFFFF")
            });
    }

    private static ThemePalette CreateFluentPalette(bool dark)
    {
        if (dark)
        {
            return CreatePalette(
                new[]
                {
                    ("ContentMaterialStrokeBrush", "#33FFFFFF"),
                    ("LiquidGlassSurfaceBrush", "#FF2B2B2B"),
                    ("LiquidGlassToolbarBrush", "#FF2B2B2B"),
                    ("LiquidGlassRowBrush", "#FF2F2F2F"),
                    ("LiquidGlassControlCheckedBrush", "#553C9DFF"),
                    ("LiquidGlassHighlightBrush", "#24FFFFFF"),
                    ("LiquidGlassHoverBrush", "#1AFFFFFF"),
                    ("LiquidGlassSelectedBrush", "#423C9DFF"),
                    ("LiquidGlassStrokeBrush", "#30FFFFFF"),
                    ("LiquidGlassShadowStrokeBrush", "#66000000"),
                    ("LiquidGlassCastShadowBrush", "#52000000"),
                    ("LiquidGlassTextPrimaryBrush", "#FFFFFFFF"),
                    ("LiquidGlassTextSecondaryBrush", "#C8FFFFFF"),
                    ("LiquidGlassAccentBrush", "#FF3C9DFF"),
                    ("LiquidGlassAccentSoftBrush", "#303C9DFF"),
                    ("LiquidGlassChromaticWarmStrokeBrush", "#00000000"),
                    ("LiquidGlassChromaticCoolStrokeBrush", "#00000000"),
                    ("LiquidGlassWarningBrush", "#FFFFB900"),
                    ("LiquidGlassReviewBrush", "#FFB18CFF")
                },
                new[]
                {
                    Gradient("LiquidGlassWindowBrush", "#FF202020", "#FF1B1B1B", "#FF202020"),
                    Gradient("LiquidGlassRegularWashBrush", "#FF2B2B2B", "#FF252525", "#FF2B2B2B"),
                    Gradient("LiquidGlassContentBackdropWashBrush", "#FF1F1F1F", "#FF1B1B1B", "#FF1F1F1F"),
                    Gradient("ContentMaterialSurfaceBrush", "#FF2B2B2B", "#FF2B2B2B", "#FF2B2B2B"),
                    Gradient("ContentMaterialRowBrush", "#FF2F2F2F", "#FF2F2F2F", "#FF2F2F2F"),
                    Gradient("LiquidGlassSidebarBrush", "#FF252525", "#FF252525", "#FF252525"),
                    Gradient("LiquidGlassDropDownBrush", "#FF2B2B2B", "#FF2B2B2B", "#FF2B2B2B"),
                    Gradient("LiquidGlassContentInfusionBrush", "#00000000", "#00000000", "#00000000", "#00000000", "#00000000"),
                    Gradient("LiquidGlassCoolContentBrush", "#00000000", "#00000000", "#00000000", "#00000000"),
                    Gradient("LiquidGlassWarmContentBrush", "#00000000", "#00000000", "#00000000", "#00000000"),
                    Gradient("LiquidGlassRefractionBrush", "#00000000", "#00000000", "#00000000", "#00000000", "#00000000"),
                    Gradient("LiquidGlassSecondaryRefractionBrush", "#00000000", "#00000000", "#00000000", "#00000000", "#00000000"),
                    Gradient("LiquidGlassSpecularBrush", "#12FFFFFF", "#00000000", "#00000000"),
                    Gradient("LiquidGlassEdgeGlowBrush", "#24FFFFFF", "#00000000", "#24FFFFFF"),
                    Gradient("LiquidGlassRimBrush", "#24FFFFFF", "#00000000", "#00000000", "#24FFFFFF"),
                    Gradient("LiquidGlassCausticBrush", "#00000000", "#00000000", "#00000000", "#00000000", "#00000000"),
                    Gradient("LiquidGlassDistortionBrush", "#00000000", "#00000000", "#00000000", "#00000000", "#00000000", "#00000000", "#00000000")
                });
        }

        return CreatePalette(
            new[]
            {
                ("ContentMaterialStrokeBrush", "#1F000000"),
                ("LiquidGlassSurfaceBrush", "#FFFFFFFF"),
                ("LiquidGlassToolbarBrush", "#FFFFFFFF"),
                ("LiquidGlassRowBrush", "#FFFFFFFF"),
                ("LiquidGlassControlCheckedBrush", "#1A0067C0"),
                ("LiquidGlassHighlightBrush", "#FFFFFFFF"),
                ("LiquidGlassHoverBrush", "#0F000000"),
                ("LiquidGlassSelectedBrush", "#180067C0"),
                ("LiquidGlassStrokeBrush", "#26000000"),
                ("LiquidGlassShadowStrokeBrush", "#24000000"),
                ("LiquidGlassCastShadowBrush", "#20000000"),
                ("LiquidGlassTextPrimaryBrush", "#FF1A1A1A"),
                ("LiquidGlassTextSecondaryBrush", "#B3000000"),
                ("LiquidGlassAccentBrush", "#FF0067C0"),
                ("LiquidGlassAccentSoftBrush", "#1A0067C0"),
                ("LiquidGlassChromaticWarmStrokeBrush", "#00FFFFFF"),
                ("LiquidGlassChromaticCoolStrokeBrush", "#00FFFFFF"),
                ("LiquidGlassWarningBrush", "#FFFFB900"),
                ("LiquidGlassReviewBrush", "#FF8764B8")
            },
            new[]
            {
                Gradient("LiquidGlassWindowBrush", "#FFF3F3F3", "#FFF3F3F3", "#FFF3F3F3"),
                Gradient("LiquidGlassRegularWashBrush", "#FFFFFFFF", "#FFFFFFFF", "#FFFFFFFF"),
                Gradient("LiquidGlassContentBackdropWashBrush", "#FFF3F3F3", "#FFF3F3F3", "#FFF3F3F3"),
                Gradient("ContentMaterialSurfaceBrush", "#FFFFFFFF", "#FFFFFFFF", "#FFFFFFFF"),
                Gradient("ContentMaterialRowBrush", "#FFFFFFFF", "#FFFFFFFF", "#FFFFFFFF"),
                Gradient("LiquidGlassSidebarBrush", "#FFFAFAFA", "#FFFAFAFA", "#FFFAFAFA"),
                Gradient("LiquidGlassDropDownBrush", "#FFFFFFFF", "#FFFFFFFF", "#FFFFFFFF"),
                Gradient("LiquidGlassContentInfusionBrush", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF"),
                Gradient("LiquidGlassCoolContentBrush", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF"),
                Gradient("LiquidGlassWarmContentBrush", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF"),
                Gradient("LiquidGlassRefractionBrush", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF"),
                Gradient("LiquidGlassSecondaryRefractionBrush", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF"),
                Gradient("LiquidGlassSpecularBrush", "#0F000000", "#00FFFFFF", "#00FFFFFF"),
                Gradient("LiquidGlassEdgeGlowBrush", "#1F000000", "#00FFFFFF", "#1F000000"),
                Gradient("LiquidGlassRimBrush", "#1F000000", "#00FFFFFF", "#00FFFFFF", "#1F000000"),
                Gradient("LiquidGlassCausticBrush", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF"),
                Gradient("LiquidGlassDistortionBrush", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF", "#00FFFFFF")
            });
    }

    private static ThemePalette CreatePalette(
        (string Key, string Color)[] solids,
        (string Key, string[] Colors)[] gradients)
    {
        var solidMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var solid in solids)
        {
            solidMap[solid.Key] = solid.Color;
        }

        var gradientMap = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var gradient in gradients)
        {
            gradientMap[gradient.Key] = gradient.Colors;
        }

        return new ThemePalette(solidMap, gradientMap);
    }

    private static (string Key, string[] Colors) Gradient(string key, params string[] colors)
    {
        return (key, colors);
    }

    private static void SetSolid(ResourceDictionary dictionary, string key, string color)
    {
        if (dictionary.TryGetValue(key, out var value) && value is SolidColorBrush brush)
        {
            brush.Color = ParseColor(color);
            return;
        }

        dictionary[key] = new SolidColorBrush(ParseColor(color));
    }

    private static void SetGradient(ResourceDictionary dictionary, string key, string[] colors)
    {
        if (!dictionary.TryGetValue(key, out var value) || value is not LinearGradientBrush brush)
        {
            brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            dictionary[key] = brush;
        }

        while (brush.GradientStops.Count < colors.Length)
        {
            brush.GradientStops.Add(new GradientStop());
        }

        while (brush.GradientStops.Count > colors.Length)
        {
            brush.GradientStops.RemoveAt(brush.GradientStops.Count - 1);
        }

        for (var i = 0; i < colors.Length; i++)
        {
            brush.GradientStops[i].Color = ParseColor(colors[i]);
            brush.GradientStops[i].Offset = colors.Length == 1 ? 0 : (double)i / (colors.Length - 1);
        }
    }

    private static Color ParseColor(string value)
    {
        var normalized = value.Trim().TrimStart('#');
        if (normalized.Length == 6)
        {
            normalized = "FF" + normalized;
        }

        return Color.FromArgb(
            Convert.ToByte(normalized[..2], 16),
            Convert.ToByte(normalized[2..4], 16),
            Convert.ToByte(normalized[4..6], 16),
            Convert.ToByte(normalized[6..8], 16));
    }
}
