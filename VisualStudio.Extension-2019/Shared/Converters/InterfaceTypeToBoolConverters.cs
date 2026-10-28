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
    public class EthernetInterfaceTypeToBoolConverter: IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool available = false;

            if(value != null)
            {
                if(((NetworkInterfaceType)value) == NetworkInterfaceType.Ethernet)
                {
                    available = true;
                }
            }

            return available;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class Wireless80211InterfaceTypeToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool available = false;

            if (value != null)
            {
                if (((NetworkInterfaceType)value) == NetworkInterfaceType.Wireless80211)
                {
                    available = true;
                }
            }

            return available;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}
