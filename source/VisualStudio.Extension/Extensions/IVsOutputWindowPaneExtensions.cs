//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.Shell.Interop;
using System;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public static class IVsOutputWindowPaneExtensions
    {
        /// <summary>
        /// Writes text to the output window pane followed by a line terminator
        /// </summary>
        /// <param name="pane"></param>
        /// <param name="pszOutputString">Text to be appended to the output window pane.</param>
        public static void OutputStringAsLine(this IVsOutputWindowPane pane, string pszOutputString)
        {
            pane.OutputString(pszOutputString + Environment.NewLine);
        }
    }
}
