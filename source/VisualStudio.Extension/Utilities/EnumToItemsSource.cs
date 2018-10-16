//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Markup;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Given an enum with items decorated with <see cref="DisplayAttribute"/> it provides an item source with the enum value and a description as string.
    /// </summary>
    public class ByteEnumToItemsSource : MarkupExtension
    {
        private readonly Type _type;

        public ByteEnumToItemsSource(Type type)
        {
            _type = type;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return Enum.GetValues(_type)
                .Cast<object>()
                .Select(e => new
                {
                    Value = (byte)e,
                    DisplayName = (new Func<string>(() =>
                    {
                        DisplayAttribute attribute = e.GetType()
                            .GetField(e.ToString())
                            .GetCustomAttributes(typeof(DisplayAttribute), false)
                            .SingleOrDefault() as DisplayAttribute;
                        return attribute == null ? e.ToString() : attribute.Description;
                    })).Invoke()
                });
        }
    }
}
