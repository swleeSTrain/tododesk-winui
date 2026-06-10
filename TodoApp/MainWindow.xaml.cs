using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TodoApp;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x00000002;

    public MainWindow()
    {
        InitializeComponent();

        var requestedTheme = GetRequestedThemeOverride();
        RootLayout.RequestedTheme = requestedTheme;
        RootFrame.RequestedTheme = requestedTheme;
        var visualTheme = VisualThemeManager.LoadSavedTheme();
        ApplyWindowTreatment(visualTheme);
        VisualThemeManager.ThemeApplied += ApplyWindowTreatment;
        RootFrame.ActualThemeChanged += (_, _) => ApplyWindowTreatment(VisualThemeManager.CurrentTheme);
        Closed += (_, _) => VisualThemeManager.ThemeApplied -= ApplyWindowTreatment;

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    private static ElementTheme GetRequestedThemeOverride()
    {
        var themeName = GetThemeNameFromCommandLine()
            ?? Environment.GetEnvironmentVariable("TODOAPP_THEME");

        return themeName?.Trim().ToLowerInvariant() switch
        {
            "dark" => ElementTheme.Dark,
            "light" => ElementTheme.Light,
            _ => ElementTheme.Default
        };
    }

    private static string? GetThemeNameFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--theme", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            const string equalsPrefix = "--theme=";
            if (arg.StartsWith(equalsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[equalsPrefix.Length..];
            }

            const string slashPrefix = "/theme:";
            if (arg.StartsWith(slashPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[slashPrefix.Length..];
            }
        }

        return null;
    }

    private void ApplyWindowTreatment(AppVisualTheme theme)
    {
        var definition = VisualThemeManager.GetDefinition(theme);
        var useCustomTitleBar = definition.UsesCustomTitleBar;
        CustomTitleBarHost.Visibility = useCustomTitleBar ? Visibility.Visible : Visibility.Collapsed;
        ExtendsContentIntoTitleBar = useCustomTitleBar;
        SetTitleBar(useCustomTitleBar ? AppTitleBar : null);
        SystemBackdrop = definition.UsesAcrylicBackdrop ? new DesktopAcrylicBackdrop() : null;
        ApplyTitleBar(definition);
        ApplyWindowAlpha(definition.WindowAlpha);
        LiquidBackdropEffect.RefreshTree(RootLayout);
    }

    private void ApplyTitleBar(ThemeDefinition definition)
    {
        var titleBar = AppWindow.TitleBar;
        var transparent = Color.FromArgb(0, 0, 0, 0);
        var isDark = RootLayout?.ActualTheme == ElementTheme.Dark;

        if (definition.WindowTreatment == AppWindowTreatment.Opaque)
        {
            var background = isDark
                ? Color.FromArgb(255, 32, 32, 32)
                : Color.FromArgb(255, 243, 243, 243);
            var inactiveBackground = isDark
                ? Color.FromArgb(255, 32, 32, 32)
                : Color.FromArgb(255, 243, 243, 243);
            var foreground = isDark
                ? Color.FromArgb(255, 255, 255, 255)
                : Color.FromArgb(255, 26, 26, 26);
            var inactiveForeground = isDark
                ? Color.FromArgb(135, 255, 255, 255)
                : Color.FromArgb(120, 26, 26, 26);

            titleBar.BackgroundColor = background;
            titleBar.InactiveBackgroundColor = inactiveBackground;
            titleBar.ButtonBackgroundColor = background;
            titleBar.ButtonInactiveBackgroundColor = inactiveBackground;
            titleBar.ButtonHoverBackgroundColor = isDark
                ? Color.FromArgb(255, 54, 54, 54)
                : Color.FromArgb(255, 229, 229, 229);
            titleBar.ButtonPressedBackgroundColor = isDark
                ? Color.FromArgb(255, 70, 70, 70)
                : Color.FromArgb(255, 210, 210, 210);
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = inactiveForeground;
            return;
        }

        titleBar.BackgroundColor = transparent;
        titleBar.InactiveBackgroundColor = transparent;
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;

        titleBar.ButtonHoverBackgroundColor = isDark
            ? Color.FromArgb(46, 255, 255, 255)
            : Color.FromArgb(34, 255, 255, 255);
        titleBar.ButtonPressedBackgroundColor = isDark
            ? Color.FromArgb(64, 255, 255, 255)
            : Color.FromArgb(48, 10, 132, 255);
        titleBar.ButtonForegroundColor = isDark
            ? Color.FromArgb(255, 246, 250, 255)
            : Color.FromArgb(255, 20, 27, 34);
        titleBar.ButtonInactiveForegroundColor = isDark
            ? Color.FromArgb(150, 246, 250, 255)
            : Color.FromArgb(150, 20, 27, 34);
    }

    private void ApplyWindowAlpha(byte alpha)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var extendedStyle = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, extendedStyle | WsExLayered);
        SetLayeredWindowAttributes(hwnd, 0, alpha, LwaAlpha);
    }

    private void BackdropDistortion_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            LiquidBackdropEffect.Attach(element, element.Tag as string);
        }
    }

    private void BackdropDistortion_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            LiquidBackdropEffect.Detach(element);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);
}
