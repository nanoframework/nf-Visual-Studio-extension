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
    public class AuthenticationTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
            {
                switch ((AuthenticationType)value)
                {
                    case AuthenticationType.None:
                        return "0";
                    case AuthenticationType.EAP:
                        return "1";
                    case AuthenticationType.Open:
                        return "4";
                    case AuthenticationType.PEAP:
                        return "2";
                    case AuthenticationType.Shared:
                        return "5";
                    case AuthenticationType.WCN:
                        return "3";
                    case AuthenticationType.WEP:
                        return "6";
                    case AuthenticationType.WPA:
                        return "7";
                    case AuthenticationType.WPA2:
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
                // 
                case "0":
                    return AuthenticationType.None;
                case "1":
                    return AuthenticationType.EAP;
                case "4":
                    return AuthenticationType.Open;
                case "2":
                    return AuthenticationType.PEAP;
                case "5":
                    return AuthenticationType.Shared;
                case "3":
                    return AuthenticationType.WCN;
                case "6":
                    return AuthenticationType.WEP;
                case "7":
                    return AuthenticationType.WPA;
                case "8":
                    return AuthenticationType.WPA2;

                default:
                    return value;
            }
        }
    }
}
