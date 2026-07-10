using Microsoft.Windows.ApplicationModel.Resources;

namespace TodoApp;

internal static class AppResources
{
    private static readonly ResourceLoader ResourceLoader = new();

    public static string Get(string key)
    {
        try
        {
            var value = ResourceLoader.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return key;
        }
    }

    public static string Format(string key, params object?[] arguments)
    {
        return string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key), arguments);
    }
}
