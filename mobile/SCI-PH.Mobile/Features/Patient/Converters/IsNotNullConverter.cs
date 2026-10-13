using System.Globalization;

namespace SCI_PH.Mobile.Features.Patient.Converters;

/// <summary>true si el valor no es null ni una cadena vacía; para mostrar secciones y campos opcionales.</summary>
public sealed class IsNotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && value is not string { Length: 0 };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
