//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.Shell.Interop;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public static class IVsStatusbarExtensions
    {
        public static void Update(this IVsStatusbar statusBar, string text, bool showBusyAnimation = false)
        {
            // stock general animation icon
            object icon = (short)Microsoft.VisualStudio.Shell.Interop.Constants.SBAI_General;

            // Make sure the status bar is not frozen  
            int frozen;
            statusBar.IsFrozen(out frozen);

            if (frozen != 0)
            {
                statusBar.FreezeOutput(0);
            }

            statusBar.SetText(text);

            statusBar.Animation(showBusyAnimation ? 1 : 0, ref icon);
        }

        public static void Clear(this IVsStatusbar statusBar)
        {
            // stock general animation icon
            object icon = (short)Microsoft.VisualStudio.Shell.Interop.Constants.SBAI_General;

            // Make sure the status bar is not frozen  
            int frozen;
            statusBar.IsFrozen(out frozen);

            if (frozen != 0)
            {
                statusBar.FreezeOutput(0);
            }

            // stop the animation
            statusBar?.Animation(0, ref icon);

            //statusBar.Clear();
            statusBar.SetText(string.Empty);
        }
    }
}
