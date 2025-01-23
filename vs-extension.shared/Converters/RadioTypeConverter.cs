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
    public class RadioTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
            {
                switch ((RadioType)value)
                {
                    case RadioType._802_11a:
                        return "1";
                    case RadioType._802_11b:
                        return "2";
                    case RadioType._802_11g:
                        return "4";
                    case RadioType._802_11n:
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
                case "1":
                    return RadioType._802_11a;
                case "2":
                    return RadioType._802_11b;
                case "4":
                    return RadioType._802_11g;
                case "8":
                    return RadioType._802_11n;

                default:
                    return value;
            }
        }
    }
}
