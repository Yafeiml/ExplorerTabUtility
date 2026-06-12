using System;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Globalization;
using System.ComponentModel;
using ExplorerTabUtility.UI.Localization;

namespace ExplorerTabUtility.UI.Converters;

public class EnumDescriptionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return DependencyProperty.UnsetValue;

        if (value is Enum enumValue)
        {
            return string.Equals(parameter as string, "Description", StringComparison.OrdinalIgnoreCase)
                ? LocalizationManager.GetEnumDescription(enumValue)
                : LocalizationManager.GetEnumName(enumValue);
        }

        var valueStr = value.ToString()!;
        var fieldInfo = value.GetType().GetField(valueStr);
        if (fieldInfo == null) return valueStr;

        var descriptionAttribute = fieldInfo.GetCustomAttributes(typeof(DescriptionAttribute), false)
            .FirstOrDefault() as DescriptionAttribute;

        return descriptionAttribute?.Description ?? valueStr;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}
