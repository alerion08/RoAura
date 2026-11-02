using System.Globalization;
using System.Windows.Data;
using RoAura.Services;
namespace RoAura;
public sealed class QuestStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => LocalizationService.Get("QState" + value);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
