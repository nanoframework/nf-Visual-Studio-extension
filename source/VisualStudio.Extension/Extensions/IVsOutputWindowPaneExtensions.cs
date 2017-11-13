//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Diagnostics;

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

        public static void DebugMessage(this IVsOutputWindowPane pane, string message)
        {
            pane.OutputStringThreadSafe(message == null ? "" : message);
        }

        public static void InternalErrorMessage(this IVsOutputWindowPane pane, string message)
        {
            pane.InternalErrorMessage(false, message);
        }

        public static void InternalErrorMessage(this IVsOutputWindowPane pane, bool assertion, string message)
        {
            pane.InternalErrorMessage(assertion, message, -1);
        }

        public static void InternalErrorMessage(this IVsOutputWindowPane pane, bool assertion, string message, int skipFrames)
        {
            if (!assertion)
            {
                message = String.IsNullOrEmpty(message) ? "Unknown Error" : message;

                if (skipFrames >= 0)
                {
                    StackTrace st = new StackTrace(skipFrames + 1, true);
                    pane.OutputStringThreadSafe(String.Format("[@ {0}: {1} @]", message, st.ToString()));
                }
                else
                {
                    pane.OutputStringThreadSafe("[@ " + message + " @]");
                }
            }
        }
    }
}
