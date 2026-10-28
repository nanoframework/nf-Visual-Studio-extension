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
    public class EncryptionTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
            {
                switch ((EncryptionType)value)
                {
                    case EncryptionType.None:
                        return "0";
                    case EncryptionType.Certificate:
                        return "6";
                    case EncryptionType.WEP:
                        return "1";
                    case EncryptionType.WPA:
                        return "2";
                    case EncryptionType.WPA2:
                        return "3";
                    case EncryptionType.WPA_PSK:
                        return "4";
                    case EncryptionType.WPA2_PSK2:
                        return "5";

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
                case "0":
                    return EncryptionType.None;
                case "6":
                    return EncryptionType.Certificate;
                case "1":
                    return EncryptionType.WEP;
                case "2":
                    return EncryptionType.WPA;
                case "3":
                    return EncryptionType.WPA2;
                case "4":
                    return EncryptionType.WPA_PSK;
                case "5":
                    return EncryptionType.WPA2_PSK2;

                default:
                    return value;
            }
        }
    }
}
