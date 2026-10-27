//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Shows, once per extension major version, a notice informing that telemetry is collected and how to opt out:
    /// a few lines in the nanoFramework output pane and an InfoBar in the main window.
    /// </summary>
    internal sealed class TelemetryNotice : IVsInfoBarUIEvents
    {
        private const string LearnMoreAction = "learnMore";
        private const string OptOutAction = "optOut";

        private uint _eventsCookie;

        private TelemetryNotice()
        {
        }

        /// <summary>
        /// Shows the notice, if telemetry is enabled and it wasn't shown for this major version yet.
        /// </summary>
        internal static async Task ShowIfNeededAsync(AsyncPackage package, Version extensionVersion)
        {
            try
            {
                if (!ShouldShowNotice(extensionVersion, ExtensionTelemetry.IsEnabled, TelemetrySetup.NanoFrameworkDirectory))
                {
                    return;
                }

                MessageCentre.OutputMessage("Telemetry: the .NET nanoFramework extension collects anonymous usage data (features used, boards and firmware versions, operation outcomes and errors) to help improve it.");
                MessageCentre.OutputMessage($"No file paths, project names, serial ports, serial numbers or other personal data are collected. To opt out set the environment variable {ExtensionTelemetry.OptOutEnvironmentVariable}=1 (or {ExtensionTelemetry.DoNotTrackEnvironmentVariable}=1) and restart Visual Studio.");
                MessageCentre.OutputMessage($"Read more at {TelemetrySetup.NoticeUrl}");
                MessageCentre.OutputMessage(Environment.NewLine);

                await new TelemetryNotice().ShowInfoBarAsync(package);
            }
            catch
            {
                // the notice is best effort
            }
        }

        /// <summary>
        /// Checks the sentinel file for the extension major version, creating it if missing.
        /// </summary>
        /// <returns><see langword="true"/> if the notice has to be shown.</returns>
        internal static bool ShouldShowNotice(Version extensionVersion, bool telemetryEnabled, string sentinelDirectory)
        {
            if (!telemetryEnabled)
            {
                return false;
            }

            try
            {
                string sentinelPath = Path.Combine(sentinelDirectory, $"telemetry-notice-vs-v{extensionVersion?.Major ?? 0}");

                if (File.Exists(sentinelPath))
                {
                    return false;
                }

                Directory.CreateDirectory(sentinelDirectory);
                File.WriteAllText(sentinelPath, DateTime.UtcNow.ToString("O"));

                return true;
            }
            catch
            {
                // can't persist the sentinel: skip the notice rather than showing it on every start
                return false;
            }
        }

        private async Task ShowInfoBarAsync(AsyncPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (await package.GetServiceAsync(typeof(SVsShell)) is not IVsShell shell
                || await package.GetServiceAsync(typeof(SVsInfoBarUIFactory)) is not IVsInfoBarUIFactory infoBarUIFactory)
            {
                return;
            }

            if (shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out object hostObject) != 0
                || hostObject is not IVsInfoBarHost host)
            {
                return;
            }

            var model = new InfoBarModel(
                new[]
                {
                    new InfoBarTextSpan(".NET nanoFramework extension collects anonymous usage data to help improve it. "),
                },
                new[]
                {
                    new InfoBarHyperlink("Learn more", LearnMoreAction),
                    new InfoBarHyperlink("Opt out (also opts out nanoff)", OptOutAction),
                },
                KnownMonikers.StatusInformation,
                isCloseButtonVisible: true);

            IVsInfoBarUIElement element = infoBarUIFactory.CreateInfoBar(model);
            element.Advise(this, out _eventsCookie);

            host.AddInfoBar(element);
        }

        public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            infoBarUIElement.Unadvise(_eventsCookie);
        }

        public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            switch (actionItem.ActionContext as string)
            {
                case LearnMoreAction:
                    try
                    {
                        Process.Start(new ProcessStartInfo(TelemetrySetup.NoticeUrl) { UseShellExecute = true });
                    }
                    catch
                    {
                        // nothing to do here
                    }
                    break;

                case OptOutAction:
                    OptOut();
                    infoBarUIElement.Close();
                    break;
            }
        }

        /// <summary>
        /// Stops the telemetry for this session and persists the opt out in the user environment variables,
        /// which also opts out nanoff.
        /// </summary>
        private static void OptOut()
        {
            Environment.SetEnvironmentVariable(ExtensionTelemetry.OptOutEnvironmentVariable, "1");

            // stop recording right away: this disables telemetry synchronously and doesn't wait for the providers to be disposed
            TelemetrySetup.Shutdown(TimeSpan.Zero);

            MessageCentre.OutputMessage($"Telemetry: opted out. The environment variable {ExtensionTelemetry.OptOutEnvironmentVariable}=1 was set for the current user.");

            // setting a user environment variable broadcasts a settings change to all windows, which can take a while
            _ = Task.Run(() =>
            {
                try
                {
                    Environment.SetEnvironmentVariable(ExtensionTelemetry.OptOutEnvironmentVariable, "1", EnvironmentVariableTarget.User);
                }
                catch
                {
                    MessageCentre.OutputMessage($"Telemetry: failed to set the environment variable {ExtensionTelemetry.OptOutEnvironmentVariable}. Please set it manually to opt out permanently.");
                }
            });
        }
    }
}
