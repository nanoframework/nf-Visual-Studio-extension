//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger;
using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace nanoFramework.Tools.VisualStudio.Extension.Converters
{
    public class MacAddressConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
            {
                byte[] macAddress = (byte[])value;

                return String.Join(":", macAddress.Select(a => a.ToString("X2")));
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var newMACAddress = value as string;
            var newMACAddressArray = newMACAddress.Split(':');

            return newMACAddressArray.Select(a => byte.Parse(a, System.Globalization.NumberStyles.HexNumber)).ToArray();
        }
    }
}
