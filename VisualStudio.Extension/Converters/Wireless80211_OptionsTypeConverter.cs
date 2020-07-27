//
// Copyright (c) 2020 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger;
using System;
using System.Globalization;
using System.Windows.Data;

namespace nanoFramework.Tools.VisualStudio.Extension.Converters
{
    public class Wireless80211_OptionsTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
            {
                switch ((Wireless80211_ConfigurationOptions)value)
                {
                    case Wireless80211_ConfigurationOptions.None:
                        return "0";
                    case Wireless80211_ConfigurationOptions.Disable:
                        return "1";
                    case Wireless80211_ConfigurationOptions.Enable:
                        return "2";
                    case Wireless80211_ConfigurationOptions.AutoConnect:
                        return "6";
                    case Wireless80211_ConfigurationOptions.SmartConfig:
                        return "10";

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
                    return Wireless80211_ConfigurationOptions.None;
                case "1":
                    return Wireless80211_ConfigurationOptions.Disable;
                case "2":
                    return Wireless80211_ConfigurationOptions.Enable;
                case "6":
                    return Wireless80211_ConfigurationOptions.AutoConnect;
                case "10":
                    return Wireless80211_ConfigurationOptions.SmartConfig;

                default:
                    return value;
            }
        }
    }
}
