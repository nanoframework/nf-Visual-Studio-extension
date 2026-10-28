//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Properties;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Records a <c>ProjectLoaded</c> event when a nanoFramework project is loaded: its output type and whether it's a unit test project.
    /// No project names or paths are recorded.
    /// </summary>
    /// <remarks>
    /// Projects are usually loaded before the package (and so telemetry) is initialized.
    /// Those events are held in memory and sent by <see cref="SendPendingEvents"/> once telemetry is enabled,
    /// so that nothing is sent before the telemetry notice is shown.
    /// </remarks>
    [Export(ExportContractNames.Scopes.UnconfiguredProject, typeof(IProjectDynamicLoadComponent))]
    [AppliesTo(NanoCSharpProjectUnconfigured.UniqueCapability)]
    internal sealed class ProjectLoadTelemetry : IProjectDynamicLoadComponent
    {
        // keep the pending list bounded, in case the package is never loaded in a long session
        private const int MaxPendingEvents = 50;

        private static readonly object s_lock = new object();

        // projects already reported in this session, kept in memory only
        private static readonly HashSet<string> s_projectsSeen = new HashSet<string>();

        // events recorded before telemetry was enabled
        private static readonly List<TelemetryProperties> s_pendingEvents = new List<TelemetryProperties>();

        private readonly UnconfiguredProject _unconfiguredProject;

        [ImportingConstructor]
        public ProjectLoadTelemetry(UnconfiguredProject unconfiguredProject)
        {
            _unconfiguredProject = unconfiguredProject;
        }

        public Task LoadAsync()
        {
            if (!ExtensionTelemetry.IsOptedOut)
            {
                // don't hold the project load
                _ = Task.Run(TrackProjectLoadedAsync);
            }

            return Task.CompletedTask;
        }

        public Task UnloadAsync() => Task.CompletedTask;

        /// <summary>
        /// Sends the events recorded before telemetry was enabled. Call once telemetry is initialized.
        /// </summary>
        internal static void SendPendingEvents()
        {
            List<TelemetryProperties> pendingEvents;

            lock (s_lock)
            {
                pendingEvents = new List<TelemetryProperties>(s_pendingEvents);
                s_pendingEvents.Clear();
            }

            if (!ExtensionTelemetry.IsEnabled)
            {
                return;
            }

            foreach (TelemetryProperties properties in pendingEvents)
            {
                ExtensionTelemetry.TrackEvent("ProjectLoaded", properties);
            }
        }

        private async Task TrackProjectLoadedAsync()
        {
            try
            {
                lock (s_lock)
                {
                    if (!s_projectsSeen.Add(_unconfiguredProject.FullPath ?? string.Empty))
                    {
                        return;
                    }
                }

                ConfiguredProject configuredProject = await _unconfiguredProject.GetSuggestedConfiguredProjectAsync();
                IProjectProperties properties = configuredProject?.Services.ProjectPropertiesProvider?.GetCommonProperties();

                if (properties is null)
                {
                    return;
                }

                string outputType = await properties.GetEvaluatedPropertyValueAsync("OutputType");
                string isTestProject = await properties.GetEvaluatedPropertyValueAsync("IsTestProject");

                var eventProperties = new TelemetryProperties
                {
                    { "output_type", outputType },
                    { "is_test_project", string.Equals(isTestProject, "true", System.StringComparison.OrdinalIgnoreCase) },
                };

                lock (s_lock)
                {
                    if (!ExtensionTelemetry.IsEnabled)
                    {
                        if (s_pendingEvents.Count < MaxPendingEvents)
                        {
                            s_pendingEvents.Add(eventProperties);
                        }

                        return;
                    }
                }

                ExtensionTelemetry.TrackEvent("ProjectLoaded", eventProperties);
            }
            catch
            {
                // telemetry must never break the extension
            }
        }
    }
}
