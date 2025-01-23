//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger;
using System;
using System.Globalization;
using System.Windows.Data;

namespace nanoFramework.Tools.VisualStudio.Extension.Converters
{
    public class NetworkInterfaceTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
            {
                switch ((NetworkInterfaceType)value)
                {
                    case NetworkInterfaceType.Ethernet:
                        return "6";

                    case NetworkInterfaceType.Wireless80211:
                        return "71";

                    default:
                        return null;
                }
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value?.ToString())
            {
                // 
                case "6":
                    return NetworkInterfaceType.Ethernet;
                case "71":
                    return NetworkInterfaceType.Wireless80211;

                default:
                    return value;
            }
        }
    }
}
