//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Wires the OpenTelemetry pipeline that exports the extension telemetry to Azure Monitor (Application Insights).
    /// Mirrors nanoff's TelemetrySetup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Telemetry is only enabled when a connection string is configured and the user hasn't opted out
    /// (see <see cref="ExtensionTelemetry.IsOptedOut"/>). When disabled, no provider is built and nothing is sent.
    /// </para>
    /// <para>
    /// This is the only class that references OpenTelemetry types. Its fields hold them as <see cref="object"/> and its
    /// non-private members only use BCL types, so that a failure to load the OpenTelemetry assemblies in a given
    /// Visual Studio version surfaces as an exception caught by the callers, instead of breaking the extension.
    /// </para>
    /// </remarks>
    internal static class TelemetrySetup
    {
        internal const string ServiceName = "nanoframework-vs";
        internal const string ActivitySourceName = ServiceName;
        internal const string CategoryName = "nanoFramework.Tools.VisualStudio.Extension";

        internal const string NoticeUrl = "https://github.com/nanoframework/nf-Visual-Studio-extension#telemetry";

        private const string CustomEventNameAttribute = "microsoft.custom_event.name";

        // Azure Monitor exporter overrides for the request name and result code
        private const string RequestNameTag = "microsoft.request.name";
        private const string RequestResultCodeTag = "microsoft.request.resultCode";

#if DEV16
        internal const string ExtensionFlavor = "vs2019";
#else
        internal const string ExtensionFlavor = "vs2022";
#endif

        internal static readonly string NanoFrameworkDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nanoFramework");

        private static readonly object s_lock = new object();

        // OpenTelemetry objects are held as object, see remarks
        private static object s_activitySource;
        private static IDisposable s_tracerProvider;
        private static IDisposable s_loggerFactory;
        private static object s_logger;

        /// <summary>
        /// Gets a value indicating whether the telemetry pipeline is active.
        /// </summary>
        internal static bool IsEnabled { get; private set; }

        /// <summary>
        /// Builds the telemetry pipeline, if a connection string is available and the user hasn't opted out.
        /// </summary>
        /// <param name="connectionString">Application Insights connection string.</param>
        /// <param name="version">Extension informational version.</param>
        /// <param name="vsVersion">Visual Studio version (major.minor).</param>
        /// <returns><see langword="true"/> if telemetry is enabled.</returns>
        internal static bool Initialize(string connectionString, string version, string vsVersion)
        {
            lock (s_lock)
            {
                if (IsEnabled)
                {
                    return true;
                }

                if (ExtensionTelemetry.IsOptedOut
                    || string.IsNullOrWhiteSpace(connectionString)
                    || connectionString.IndexOf("InstrumentationKey=", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }

                try
                {
                    BuildPipeline(connectionString, version, GetCommonAttributes(GetOrCreateInstallId(), vsVersion));

                    IsEnabled = true;
                }
                catch
                {
                    // telemetry is not mandatory, e.g. an invalid connection string,
                    // or the OpenTelemetry assemblies can't be loaded in this Visual Studio version
                    Shutdown(TimeSpan.Zero);
                }

                return IsEnabled;
            }
        }

        /// <summary>
        /// Flushes pending telemetry, waiting at most <paramref name="timeout"/>, and tears down the pipeline.
        /// </summary>
        internal static void Shutdown(TimeSpan timeout)
        {
            IDisposable tracerProvider;
            IDisposable loggerFactory;

            lock (s_lock)
            {
                IsEnabled = false;

                tracerProvider = s_tracerProvider;
                loggerFactory = s_loggerFactory;

                s_tracerProvider = null;
                s_loggerFactory = null;
                s_logger = null;
                s_activitySource = null;
            }

            if (tracerProvider is null && loggerFactory is null)
            {
                return;
            }

            // disposing the providers flushes their pending batches;
            // bound the wait so a slow or absent network never hangs Visual Studio on close
            Task shutdown = Task.Run(() =>
            {
                try
                {
                    tracerProvider?.Dispose();
                    loggerFactory?.Dispose();
                }
                catch
                {
                    // telemetry must never break the extension
                }
            });

            try
            {
                shutdown.Wait(timeout);
            }
            catch
            {
                // telemetry must never break the extension
            }
        }

        /// <summary>
        /// Emits a custom event (a log record with the custom event name attribute).
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void EmitEvent(string name, IEnumerable<KeyValuePair<string, object>> attributes)
        {
            if (s_logger is not ILogger logger
                || !logger.IsEnabled(LogLevel.Information))
            {
                return;
            }

            logger.Log(
                LogLevel.Information,
                default,
                new TelemetryEventState(name, attributes),
                null,
                (state, _) => state.Name);
        }

        /// <summary>
        /// Starts the activity for an operation.
        /// </summary>
        /// <returns>The activity, or <see langword="null"/> if telemetry is disabled.</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static object StartActivity(string name, bool makeCurrent, out object previousActivity)
        {
            previousActivity = Activity.Current;

            if (s_activitySource is not ActivitySource source)
            {
                return null;
            }

            Activity activity = source.StartActivity(name, ActivityKind.Server);

            if (activity is null)
            {
                return null;
            }

            activity.SetTag(RequestNameTag, name);

            if (!makeCurrent)
            {
                // don't let a long running operation become the parent of unrelated events
                Activity.Current = (Activity)previousActivity;
            }

            return activity;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SetActivityTag(object activity, string key, object value) => ((Activity)activity).SetTag(key, value);

        /// <summary>
        /// Records the outcome of an operation and stops its activity, which exports it.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void StopActivity(object activityObject, string resultCode, bool success, bool wasCurrent, object previousActivity)
        {
            var activity = (Activity)activityObject;

            activity.SetTag(RequestResultCodeTag, resultCode);
            activity.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

            Activity current = Activity.Current;

            activity.Stop();

            // Stop() restores Activity.Current only when the activity is the current one in this flow;
            // when stopped from a different flow (e.g. a UI handler), make sure it doesn't leak
            if (wasCurrent && current != activity && Activity.Current == activity)
            {
                Activity.Current = (Activity)previousActivity;
            }
        }

        /// <summary>
        /// Attributes added to every telemetry item.
        /// </summary>
        internal static KeyValuePair<string, object>[] GetCommonAttributes(string installId, string vsVersion) => new KeyValuePair<string, object>[]
        {
            // mapped by the Azure Monitor exporter to user_Id, which lights up the Users/Retention views
            new KeyValuePair<string, object>("enduser.pseudo.id", installId),
            new KeyValuePair<string, object>("os.family", "Windows"),
            new KeyValuePair<string, object>("os.arch", RuntimeInformation.OSArchitecture.ToString()),
            new KeyValuePair<string, object>("dotnet.runtime", RuntimeInformation.FrameworkDescription),
            new KeyValuePair<string, object>("vs.version", vsVersion ?? "unknown"),
            new KeyValuePair<string, object>("nf.ext_flavor", ExtensionFlavor),
        };

        /// <summary>
        /// Gets the anonymous install ID: a random GUID generated on first use and persisted in the user's
        /// nanoFramework folder. It's not derived from any hardware or user information.
        /// It's the same file used by nanoff, so that a user of both tools is counted once.
        /// </summary>
        internal static string GetOrCreateInstallId() => GetOrCreateInstallId(Path.Combine(NanoFrameworkDirectory, "telemetry.id"));

        internal static string GetOrCreateInstallId(string filePath)
        {
            try
            {
                if (File.Exists(filePath)
                    && Guid.TryParse(File.ReadAllText(filePath).Trim(), out Guid existingId))
                {
                    return existingId.ToString("D");
                }

                string newId = Guid.NewGuid().ToString("D");

                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllText(filePath, newId);

                return newId;
            }
            catch
            {
                // can't persist it: use a per-session ID
                return Guid.NewGuid().ToString("D");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BuildPipeline(string connectionString, string version, KeyValuePair<string, object>[] commonAttributes)
        {
            // don't let the exporter send its own SDK usage statistics
            SetEnvironmentVariableIfUnset("APPLICATIONINSIGHTS_STATSBEAT_DISABLED", "true");
            SetEnvironmentVariableIfUnset("APPLICATIONINSIGHTS_SDKSTATS_DISABLED_ALL", "true");

            // service.instance.id is set explicitly because the exporter defaults it to the machine name
            ResourceBuilder resource = ResourceBuilder
                .CreateEmpty()
                .AddService(ServiceName, serviceVersion: version, autoGenerateServiceInstanceId: false, serviceInstanceId: "-");

            void configureExporter(AzureMonitorExporterOptions options)
            {
                options.ConnectionString = connectionString;

                // send everything, the volume is small
                options.SamplingRatio = 1.0F;
                options.TracesPerSecond = null;
                options.EnableTraceBasedLogsSampler = false;

                // none of these are wanted from inside Visual Studio
                options.EnableLiveMetrics = false;
                options.EnablePerformanceCounters = false;
                options.EnableStandardMetrics = false;

                // telemetry that fails to be sent is stored and retried later;
                // separate from nanoff's folder so that the two processes don't compete for the same files
                options.StorageDirectory = Path.Combine(NanoFrameworkDirectory, "telemetry-vs");

                // keep network retries short
                options.Retry.MaxRetries = 1;
                options.Retry.NetworkTimeout = TimeSpan.FromSeconds(10);
            }

            var activitySource = new ActivitySource(ActivitySourceName, version);

            TracerProvider tracerProvider = null;
            ILoggerFactory loggerFactory = null;

            try
            {
                tracerProvider = Sdk.CreateTracerProviderBuilder()
                    .SetResourceBuilder(resource)
                    .AddSource(ActivitySourceName)
                    .SetSampler(new AlwaysOnSampler())
                    .AddProcessor(new CommonAttributesActivityProcessor(commonAttributes))
                    .AddAzureMonitorTraceExporter(configureExporter)
                    .Build();

                loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder.SetMinimumLevel(LogLevel.Information);
                    builder.AddOpenTelemetry(options =>
                    {
                        options.SetResourceBuilder(resource);
                        options.AddProcessor(new CommonAttributesLogProcessor(commonAttributes));
                        options.AddAzureMonitorLogExporter(configureExporter);
                    });
                });
            }
            catch
            {
                tracerProvider?.Dispose();
                loggerFactory?.Dispose();
                activitySource.Dispose();

                throw;
            }

            s_activitySource = activitySource;
            s_tracerProvider = tracerProvider;
            s_loggerFactory = loggerFactory;
            s_logger = loggerFactory.CreateLogger(CategoryName);
        }

        private static void SetEnvironmentVariableIfUnset(string name, string value)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        /// <summary>
        /// Log state for a custom event. Implements <see cref="IReadOnlyList{T}"/> so that
        /// logging providers (e.g. OpenTelemetry) pick up the entries as attributes.
        /// </summary>
        internal sealed class TelemetryEventState : IReadOnlyList<KeyValuePair<string, object>>
        {
            private readonly List<KeyValuePair<string, object>> _attributes;

            public TelemetryEventState(string name, IEnumerable<KeyValuePair<string, object>> attributes)
            {
                Name = name;
                _attributes = new List<KeyValuePair<string, object>>
                {
                    new KeyValuePair<string, object>(CustomEventNameAttribute, name),
                };

                if (attributes is not null)
                {
                    foreach (KeyValuePair<string, object> attribute in attributes)
                    {
                        if (!string.IsNullOrEmpty(attribute.Key) && attribute.Value is not null)
                        {
                            _attributes.Add(attribute);
                        }
                    }
                }
            }

            public string Name { get; }

            public int Count => _attributes.Count;

            public KeyValuePair<string, object> this[int index] => _attributes[index];

            public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _attributes.GetEnumerator();

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

            public override string ToString() => Name;
        }

        /// <summary>
        /// Adds the common attributes to every activity.
        /// </summary>
        private sealed class CommonAttributesActivityProcessor : BaseProcessor<Activity>
        {
            private readonly KeyValuePair<string, object>[] _attributes;

            public CommonAttributesActivityProcessor(KeyValuePair<string, object>[] attributes) => _attributes = attributes;

            public override void OnStart(Activity data)
            {
                foreach (KeyValuePair<string, object> attribute in _attributes)
                {
                    data.SetTag(attribute.Key, attribute.Value);
                }
            }
        }

        /// <summary>
        /// Adds the common attributes to every log record.
        /// </summary>
        private sealed class CommonAttributesLogProcessor : BaseProcessor<LogRecord>
        {
            private readonly KeyValuePair<string, object>[] _attributes;

            public CommonAttributesLogProcessor(KeyValuePair<string, object>[] attributes) => _attributes = attributes;

            public override void OnEnd(LogRecord data)
            {
                var attributes = new List<KeyValuePair<string, object>>(data.Attributes ?? Array.Empty<KeyValuePair<string, object>>());
                attributes.AddRange(_attributes);
                data.Attributes = attributes;
            }
        }
    }
}
