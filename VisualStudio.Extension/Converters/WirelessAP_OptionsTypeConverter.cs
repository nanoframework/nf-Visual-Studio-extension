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
    public class WirelessAP_OptionsTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
            {
                switch ((WirelessAP_ConfigurationOptions)value)
                {
                    case WirelessAP_ConfigurationOptions.None:
                        return "0";
                    case WirelessAP_ConfigurationOptions.Disable:
                        return "1";
                    case WirelessAP_ConfigurationOptions.Enable:
                        return "2";
                    case WirelessAP_ConfigurationOptions.AutoStart:
                        return "6";
                    case WirelessAP_ConfigurationOptions.HiddenSSID:
                        return "8";

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
                case "0":
                    return WirelessAP_ConfigurationOptions.None;
                case "1":
                    return WirelessAP_ConfigurationOptions.Disable;
                case "2":
                    return WirelessAP_ConfigurationOptions.Enable;
                case "6":
                    return WirelessAP_ConfigurationOptions.AutoStart;
                case "8":
                    return WirelessAP_ConfigurationOptions.HiddenSSID;

                default:
                    return value;
            }
        }
    }
}
