//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    using System;
    using System.Drawing;
    using System.Runtime.InteropServices;
    using System.Windows;
    using System.Windows.Interop;

    /// <summary>
    /// Shows the .NET nanoFramework icon in the title bar of the dialogs.
    /// </summary>
    /// <remarks>
    /// The Visual Studio dialog windows replace the icon set with <see cref="Window.Icon"/> when they set their window style,
    /// so the icon is set on the window handle once the dialog is loaded.
    /// </remarks>
    internal static class DialogIcon
    {
        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        private static readonly Uri s_iconUri = new Uri("pack://application:,,,/nanoFramework.Tools.VS2022.Extension;component/Resources/nF_logo_128.png");

        private static Icon s_smallIcon;
        private static Icon s_bigIcon;

        /// <summary>
        /// Sets the .NET nanoFramework icon on <paramref name="window"/> when it's loaded.
        /// </summary>
        public static void Apply(Window window)
        {
            // set again once the content is rendered: sizing the dialog to its content updates the window style, which drops the icon
            window.Loaded += (sender, e) => SetIcon(window);
            window.ContentRendered += (sender, e) => SetIcon(window);
        }

        private static void SetIcon(Window window)
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;

                if (handle == IntPtr.Zero)
                {
                    return;
                }

                s_smallIcon ??= CreateIcon(16);
                s_bigIcon ??= CreateIcon(32);

                SendMessage(handle, WM_SETICON, (IntPtr)ICON_SMALL, s_smallIcon.Handle);
                SendMessage(handle, WM_SETICON, (IntPtr)ICON_BIG, s_bigIcon.Handle);
            }
            catch (Exception ex)
            {
                // the dialog just keeps the default icon
                MessageCentre.InternalErrorWriteLine($"Couldn't set the dialog icon: {ex.Message}");
            }
        }

        // the icons are kept for the lifetime of the extension, there's only a couple of them
        private static Icon CreateIcon(int size)
        {
            using var stream = Application.GetResourceStream(s_iconUri).Stream;
            using var logo = new Bitmap(stream);
            using var bitmap = new Bitmap(logo, new System.Drawing.Size(size, size));

            return Icon.FromHandle(bitmap.GetHicon());
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
