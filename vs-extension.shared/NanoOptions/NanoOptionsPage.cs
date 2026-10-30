//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using Microsoft.VisualStudio.Shell;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// An options page of the extension in Tools › Options › .NET nanoFramework, stored in <see cref="NanoSettings"/>.
    /// </summary>
    public abstract class NanoOptionsPage : DialogPage
    {
        /// <summary>
        /// Copies the values from <see cref="NanoSettings"/> to the properties of the page.
        /// </summary>
        protected abstract void LoadSettings();

        /// <summary>
        /// Copies the properties of the page to <see cref="NanoSettings"/>.
        /// </summary>
        protected abstract void SaveSettings();

        /// <summary>
        /// Applies the side effects of the saved values, if any.
        /// </summary>
        protected virtual void OnSettingsSaved()
        {
        }

        public override void LoadSettingsFromStorage()
        {
            LoadSettings();
        }

        public override void SaveSettingsToStorage()
        {
            TelemetryProperties settingsBefore = TelemetryStartup.GetSettingsSnapshot();

            SaveSettings();

            TelemetryStartup.TrackSettingsChanges(settingsBefore, TelemetryStartup.GetSettingsSnapshot());

            try
            {
                OnSettingsSaved();
            }
            catch (Exception ex)
            {
                // the settings are saved, failing to apply them mustn't stop the Options dialog from closing
                MessageCentre.InternalErrorWriteLine($"Error applying the settings of {GetType().Name}: {ex.Message}");
            }
        }
    }
}
