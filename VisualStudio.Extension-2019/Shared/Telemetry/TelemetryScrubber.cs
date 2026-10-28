//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Text.RegularExpressions;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Removes personal data (user profile paths, user name, machine name) from text before it's sent as telemetry.
    /// </summary>
    /// <remarks>
    /// Same implementation as nanoff's TelemetryScrubber (nanoFramework.Tools.FirmwareFlasher), which is internal to that library.
    /// </remarks>
    internal static class TelemetryScrubber
    {
        internal const string UserPlaceholder = "<user>";
        internal const string MachinePlaceholder = "<machine>";

        // generic home folder patterns, for paths that aren't under the current user's profile
        // (e.g. another user's folder, or a path captured on a different OS)
        private static readonly Regex s_homeFolderRegex = new Regex(
            @"(?i)([a-z]:\\(?:users|documents and settings)\\|/home/|/users/)[^\\/:*?""<>|\r\n']+",
            RegexOptions.Compiled);

        /// <summary>
        /// Scrubs <paramref name="text"/> using the current environment's user profile, user name and machine name.
        /// </summary>
        internal static string Scrub(string text) => Scrub(
            text,
            SafeGet(() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            SafeGet(() => Environment.UserName),
            SafeGet(() => Environment.MachineName));

        /// <summary>
        /// Scrubs <paramref name="text"/> using the given user profile path, user name and machine name.
        /// </summary>
        internal static string Scrub(string text, string userProfile, string userName, string machineName)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            if (!string.IsNullOrEmpty(userProfile))
            {
                text = ReplaceIgnoreCase(text, userProfile, "~");

                // also catch the same path written with the other directory separator
                text = ReplaceIgnoreCase(text, userProfile.Replace('\\', '/'), "~");
                text = ReplaceIgnoreCase(text, userProfile.Replace('/', '\\'), "~");
            }

            text = s_homeFolderRegex.Replace(text, "$1" + UserPlaceholder);

            // short names (e.g. "pi", "dev") would cause too many false positives in regular words
            if (!string.IsNullOrEmpty(userName) && userName.Length >= 3)
            {
                text = ReplaceWholeWord(text, userName, UserPlaceholder);
            }

            if (!string.IsNullOrEmpty(machineName) && machineName.Length >= 3)
            {
                text = ReplaceWholeWord(text, machineName, MachinePlaceholder);
            }

            return text;
        }

        private static string ReplaceIgnoreCase(string text, string oldValue, string newValue) =>
            Regex.Replace(text, Regex.Escape(oldValue), newValue.Replace("$", "$$"), RegexOptions.IgnoreCase);

        private static string ReplaceWholeWord(string text, string word, string newValue) =>
            Regex.Replace(text, $@"(?<![\w]){Regex.Escape(word)}(?![\w])", newValue.Replace("$", "$$"), RegexOptions.IgnoreCase);

        private static string SafeGet(Func<string> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return null;
            }
        }
    }
}
