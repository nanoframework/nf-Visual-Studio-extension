//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel
{
    /// <summary>
    /// An option of a combo box in a Remote UI dialog: a value of a debug library enum and its display name.
    /// </summary>
    /// <remarks>
    /// The combo boxes bind their <c>SelectedItem</c> to one of these, Remote UI keeps the identity of the instances.
    /// </remarks>
    [DataContract]
    internal sealed class ChoiceItem
    {
        public ChoiceItem(byte value, string displayName)
        {
            Value = value;
            DisplayName = displayName;
        }

        /// <summary>
        /// The enum value.
        /// </summary>
        public byte Value { get; }

        [DataMember]
        public string DisplayName { get; }

        public override string ToString() => DisplayName;

        /// <summary>
        /// Lists the values of <typeparamref name="TEnum"/> with their <see cref="DescriptionAttribute"/> as display name.
        /// </summary>
        /// <typeparam name="TEnum">A debug library enum, with byte values.</typeparam>
        /// <param name="currentValue">The value being edited. It's added to the list if it isn't one of the enum values (e.g. a combination of flags).</param>
        /// <param name="includeZero">List the value 0 too, for enums where it's a valid choice (e.g. no encryption). By default it's left out.</param>
        public static List<ChoiceItem> ListFor<TEnum>(TEnum currentValue, bool includeZero = false) where TEnum : struct, Enum
        {
            var items = Enum.GetValues(typeof(TEnum))
                .Cast<TEnum>()
                .Where(e => includeZero || Convert.ToByte(e) != 0)
                .Select(e => new ChoiceItem(Convert.ToByte(e), GetDisplayName(e)))
                .ToList();

            var value = Convert.ToByte(currentValue);

            if (!items.Any(i => i.Value == value))
            {
                items.Insert(0, new ChoiceItem(value, GetDisplayName(currentValue)));
            }

            return items;
        }

        /// <summary>
        /// The <see cref="DescriptionAttribute"/> of an enum value, or its name. A combination of flags lists the flags.
        /// </summary>
        public static string GetDisplayName<TEnum>(TEnum value) where TEnum : struct, Enum
        {
            if (Enum.IsDefined(typeof(TEnum), value))
            {
                var description = typeof(TEnum)
                    .GetField(value.ToString())
                    .GetCustomAttributes(typeof(DescriptionAttribute), false)
                    .OfType<DescriptionAttribute>()
                    .SingleOrDefault()?
                    .Description;

                return string.IsNullOrEmpty(description) ? value.ToString() : description;
            }

            // combination of flags: list the largest flags that are set, without repeating the ones they include
            var remaining = Convert.ToByte(value);
            var names = new List<string>();

            foreach (var flag in Enum.GetValues(typeof(TEnum)).Cast<TEnum>().OrderByDescending(e => Convert.ToByte(e)))
            {
                var flagValue = Convert.ToByte(flag);

                if (flagValue != 0
                    && (Convert.ToByte(value) & flagValue) == flagValue
                    && (remaining & flagValue) != 0)
                {
                    names.Insert(0, GetDisplayName(flag));
                    remaining &= (byte)~flagValue;
                }
            }

            return names.Count > 0 && remaining == 0 ? string.Join(" + ", names) : $"0x{Convert.ToByte(value):X2}";
        }
    }
}
