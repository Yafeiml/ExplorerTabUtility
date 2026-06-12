using System;
using System.Globalization;
using System.Windows;

namespace ExplorerTabUtility.UI.Localization;

internal static class LocalizationManager
{
    private const string SimplifiedChineseDictionary =
        "pack://application:,,,/ExplorerTabUtility;component/UI/Localization/Strings.zh-Hans.xaml";

    public static void ApplyCurrentCulture(ResourceDictionary resources)
    {
        if (!ShouldUseSimplifiedChinese(CultureInfo.CurrentUICulture))
            return;

        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(SimplifiedChineseDictionary, UriKind.Absolute)
        });
    }

    public static string GetString(string key)
    {
        return Application.Current.TryFindResource(key) as string ?? key;
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, GetString(key), args);
    }

    public static string GetEnumName(object value)
    {
        return GetString($"Enum_{value.GetType().Name}_{value}");
    }

    public static string GetEnumDescription(object value)
    {
        return GetString($"EnumDescription_{value.GetType().Name}_{value}");
    }

    private static bool ShouldUseSimplifiedChinese(CultureInfo culture)
    {
        var name = culture.Name;
        if (string.IsNullOrEmpty(name))
            return false;

        return name.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("zh-SG", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("zh-MY", StringComparison.OrdinalIgnoreCase);
    }
}
