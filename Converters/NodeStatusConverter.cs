using System.Globalization;
using System.Numerics;
using System.Windows.Data;
using System.Windows.Media;

namespace PKTWinNode.Converters
{
    public class NodeStatusToTextConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return "Unknown";

            var nodeStatus = values[0] as string;
            var hasOperationalStatus = values[1] is bool hasOp && hasOp;

            if (string.IsNullOrEmpty(nodeStatus))
                return "Unknown";

            if (nodeStatus.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                return "Online";

            if (nodeStatus.Equals("NOT YET SETUP", StringComparison.OrdinalIgnoreCase))
            {
                if (!hasOperationalStatus)
                    return "Offline";

                return "Connecting";
            }

            if (nodeStatus.StartsWith("ConnectTimeout", StringComparison.OrdinalIgnoreCase))
                return "Connection Timeout";

            return nodeStatus;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class NodeStatusToColorConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return new SolidColorBrush(Colors.Gray);

            var nodeStatus = values[0] as string;
            var hasOperationalStatus = values[1] is bool hasOp && hasOp;

            if (string.IsNullOrEmpty(nodeStatus))
                return new SolidColorBrush(Colors.Gray);

            if (nodeStatus.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#28A745"));

            if (nodeStatus.Equals("NOT YET SETUP", StringComparison.OrdinalIgnoreCase))
            {
                if (!hasOperationalStatus)
                    return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DC3545"));

                return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFC107"));
            }

            if (nodeStatus.StartsWith("ConnectTimeout", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFC107"));

            return new SolidColorBrush(Colors.Gray);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class HexYieldToDecimalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string hexValue || string.IsNullOrEmpty(hexValue))
                return "N/A";

            if (!hexValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return "N/A";

            try
            {
                var hexString = hexValue.Substring(2);
                var decimalValue = BigInteger.Parse(hexString, NumberStyles.HexNumber);
                var divisor = BigInteger.Pow(10, 18);
                var scaledValue = (double)decimalValue / (double)divisor;
                return scaledValue.ToString("F2", CultureInfo.InvariantCulture) + " PKT";
            }
            catch
            {
                return "N/A";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
