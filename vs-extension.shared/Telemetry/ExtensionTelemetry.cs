//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using nanoFramework.Tools.Debugger;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Telemetry entry point for the extension.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Operations (deploy, debug launch, firmware update, ...) are recorded with <see cref="StartOperation"/> and end up
    /// as rows in the Application Insights <c>requests</c> table. Point in time facts are recorded with
    /// <see cref="TrackEvent"/> and <see cref="TrackException"/> and end up in the <c>customEvents</c> table.
    /// </para>
    /// <para>
    /// Property values must never contain personal data: no paths, file, project or assembly names, serial ports,
    /// device IDs, serial numbers, IP addresses or exception messages.
    /// </para>
    /// <para>
    /// The members of this class don't expose any OpenTelemetry type and never throw: if the telemetry pipeline can't be
    /// loaded in the running Visual Studio, they're no-ops.
    /// </para>
    /// </remarks>
    internal static class ExtensionTelemetry
    {
        /// <summary>
        /// Environment variable that disables telemetry when set to <c>1</c> or <c>true</c>. Shared with nanoff.
        /// </summary>
        public const string OptOutEnvironmentVariable = "NANOFRAMEWORK_TELEMETRY_OPTOUT";

        /// <summary>
        /// Cross-tool convention (https://github.com/heliomass/ConsoleDoNotTrack) that disables telemetry when set to <c>1</c> or <c>true</c>.
        /// </summary>
        public const string DoNotTrackEnvironmentVariable = "DO_NOT_TRACK";

        internal const string ExceptionThrownEventName = "ExceptionThrown";

        private const int MaxStackTraceLength = 8000;
        private const int MaxValueLength = 64;

        /// <summary>
        /// Gets a value indicating whether the user has opted out of telemetry through
        /// <see cref="OptOutEnvironmentVariable"/> or <see cref="DoNotTrackEnvironmentVariable"/>.
        /// </summary>
        public static bool IsOptedOut => IsOptedOutCore(Environment.GetEnvironmentVariable);

        /// <summary>
        /// Gets a value indicating whether telemetry is being collected.
        /// </summary>
        public static bool IsEnabled => TelemetrySetup.IsEnabled;

        internal static bool IsOptedOutCore(Func<string, string> getEnvironmentVariable) =>
            IsTruthy(getEnvironmentVariable(OptOutEnvironmentVariable))
            || IsTruthy(getEnvironmentVariable(DoNotTrackEnvironmentVariable));

        /// <summary>
        /// Records a custom event.
        /// </summary>
        /// <param name="name">Event name (e.g. <c>DeviceConnected</c>).</param>
        /// <param name="properties">Optional properties. Values must not contain personal data.</param>
        public static void TrackEvent(string name, IEnumerable<KeyValuePair<string, object>> properties = null)
        {
            if (!TelemetrySetup.IsEnabled)
            {
                return;
            }

            try
            {
                TelemetrySetup.EmitEvent(name, Sanitize(properties));
            }
            catch
            {
                // telemetry must never break the extension
            }
        }

        /// <summary>
        /// Records an exception. Only the exception type, innermost exception type, HRESULT and a scrubbed stack trace
        /// are sent (user profile paths, user name and machine name are removed). Exception messages are never sent,
        /// as free-form text can't be reliably scrubbed of personal data.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <param name="stage">Short, fixed identifier of where the exception was caught (e.g. <c>deploy</c>).</param>
        /// <param name="properties">Optional extra properties. Values must not contain personal data.</param>
        public static void TrackException(Exception exception, string stage, IEnumerable<KeyValuePair<string, object>> properties = null)
        {
            if (exception is null
                || !TelemetrySetup.IsEnabled)
            {
                return;
            }

            try
            {
                var attributes = new TelemetryProperties
                {
                    { "stage", stage },
                    { "exception.type", exception.GetType().FullName },
                    { "exception.stacktrace", Truncate(TelemetryScrubber.Scrub(exception.StackTrace), MaxStackTraceLength) },
                    { "exception.hresult", exception.HResult.ToString("X8", CultureInfo.InvariantCulture) },
                };

                Exception inner = exception.InnerException;

                if (inner is not null)
                {
                    // report the innermost exception, which is usually the root cause
                    while (inner.InnerException is not null)
                    {
                        inner = inner.InnerException;
                    }

                    attributes.Add("exception.inner_type", inner.GetType().FullName);
                }

                if (properties is not null)
                {
                    attributes.AddRange(Sanitize(properties));
                }

                TelemetrySetup.EmitEvent(ExceptionThrownEventName, attributes);
            }
            catch
            {
                // telemetry must never break the extension
            }
        }

        /// <summary>
        /// Starts an operation, recorded as a row in the <c>requests</c> table when it's disposed.
        /// Always returns an instance, which is a no-op when telemetry is disabled.
        /// </summary>
        /// <param name="name">Operation name (e.g. <c>deploy</c>).</param>
        /// <param name="makeCurrent">
        /// <see langword="true"/> to make the operation the parent of the events recorded while it runs, in the same async flow.
        /// Use <see langword="false"/> for long running operations.
        /// </param>
        public static TelemetryOperation StartOperation(string name, bool makeCurrent = true) => new TelemetryOperation(name, makeCurrent);

        /// <summary>
        /// Properties that describe a device: target, platform, transport, versions and boot loader.
        /// No serial numbers, unique IDs or port names.
        /// </summary>
        public static TelemetryProperties DeviceProperties(NanoDeviceBase device)
        {
            var properties = new TelemetryProperties();

            if (device is null
                || !TelemetrySetup.IsEnabled)
            {
                return properties;
            }

            try
            {
                properties.Add("nf.target", device.TargetName);
                properties.Add("nf.platform", device.Platform);
                properties.Add("nf.transport", device.Transport.ToString());
                properties.Add("nf.is_virtual", device.Description?.StartsWith("Virtual nanoDevice", StringComparison.Ordinal) ?? false);
                properties.Add("nf.clr_version", device.CLRVersion?.ToString());
                properties.Add("nf.has_nanobooter", device.HasNanoBooter);
                properties.Add("nf.has_proprietary_booter", device.HasProprietaryBooter);

                if (device.DeviceInfo is { Valid: true } deviceInfo)
                {
                    properties.Add("nf.fw_version", deviceInfo.SolutionBuildVersion?.ToString());
                }
            }
            catch
            {
                // telemetry must never break the extension
            }

            return properties;
        }

        /// <summary>
        /// Records a click on one of the Device Explorer links, identified by a fixed name (never the URL itself).
        /// </summary>
        /// <param name="uri">The link.</param>
        /// <param name="source">Fixed name of where the link is: <c>deviceExplorer</c> or <c>helpDialog</c>.</param>
        public static void TrackHyperlinkClicked(Uri uri, string source = "deviceExplorer")
        {
            TrackEvent("HyperlinkClicked", new TelemetryProperties
            {
                { "link", GetLinkName(uri) },
                { "source", source },
            });
        }

        /// <summary>
        /// The fixed name of one of the Device Explorer links, <c>other</c> for any other link.
        /// </summary>
        public static string GetLinkName(Uri uri)
        {
            if (uri is null)
            {
                return "other";
            }

            string address = uri.Host + uri.AbsolutePath.TrimEnd('/');

            return address.ToLowerInvariant() switch
            {
                "www.nanoframework.net" => "website",
                "github.com/nanoframework/samples" => "samples",
                "docs.nanoframework.net/api/index.html" => "apiReference",
                "docs.nanoframework.net/index.html" => "docs",
                "github.com/nanoframework/home/issues" => "issues",
                "github.com/nanoframework/home" => "github",
                "marketplace.visualstudio.com/items" => "marketplace",
                "discord.gg/gcybu8t" => "discord",
                "docs.nanoframework.net/content/getting-started-guides/trouble-shooting-guide.html" => "troubleshooting",
                _ => "other",
            };
        }

        internal static object SanitizeValue(object value)
        {
            switch (value)
            {
                case null:
                    return null;

                case bool:
                case int:
                case long:
                case double:
                    return value;

                case Enum:
                    return value.ToString();

                case string text:
                    return Truncate(text, MaxValueLength);

                default:
                    return Truncate(Convert.ToString(value, CultureInfo.InvariantCulture), MaxValueLength);
            }
        }

        private static List<KeyValuePair<string, object>> Sanitize(IEnumerable<KeyValuePair<string, object>> properties)
        {
            var sanitized = new List<KeyValuePair<string, object>>();

            if (properties is null)
            {
                return sanitized;
            }

            foreach (KeyValuePair<string, object> property in properties)
            {
                object value = SanitizeValue(property.Value);

                if (!string.IsNullOrEmpty(property.Key) && value is not null)
                {
                    sanitized.Add(new KeyValuePair<string, object>(property.Key, value));
                }
            }

            return sanitized;
        }

        private static bool IsTruthy(string value) =>
            value is not null
            && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

        private static string Truncate(string value, int maxLength) =>
            value is null || value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    /// <summary>
    /// Telemetry properties, with a collection initializer friendly <see cref="Add(string, object)"/>.
    /// </summary>
    internal sealed class TelemetryProperties : List<KeyValuePair<string, object>>
    {
        public void Add(string key, object value) => Add(new KeyValuePair<string, object>(key, value));
    }

    /// <summary>
    /// An operation recorded as a row in the Application Insights <c>requests</c> table.
    /// Call <see cref="Complete"/> with the outcome and dispose it to record it.
    /// </summary>
    internal sealed class TelemetryOperation : IDisposable
    {
        public const string ResultOk = "OK";
        public const string ResultFailed = "Failed";
        public const string ResultException = "Exception";
        public const string ResultUnknown = "Unknown";

        private readonly object _activity;
        private readonly object _previousActivity;
        private readonly bool _makeCurrent;
        private string _resultCode;
        private bool _success;
        private bool _disposed;

        internal TelemetryOperation(string name, bool makeCurrent)
        {
            Name = name;
            _makeCurrent = makeCurrent;

            if (!TelemetrySetup.IsEnabled)
            {
                return;
            }

            try
            {
                _activity = TelemetrySetup.StartActivity(name, makeCurrent, out _previousActivity);
            }
            catch
            {
                // telemetry must never break the extension
            }
        }

        /// <summary>
        /// The operation name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the result code set with <see cref="Complete"/>, if any.
        /// </summary>
        public string ResultCode => _resultCode;

        /// <summary>
        /// Sets a property of the operation. The value must not contain personal data.
        /// </summary>
        public TelemetryOperation SetProperty(string key, object value)
        {
            if (_activity is null)
            {
                return this;
            }

            try
            {
                TelemetrySetup.SetActivityTag(_activity, key, ExtensionTelemetry.SanitizeValue(value));
            }
            catch
            {
                // telemetry must never break the extension
            }

            return this;
        }

        /// <summary>
        /// Sets several properties of the operation. Values must not contain personal data.
        /// </summary>
        public TelemetryOperation SetProperties(IEnumerable<KeyValuePair<string, object>> properties)
        {
            if (properties is not null)
            {
                foreach (KeyValuePair<string, object> property in properties)
                {
                    SetProperty(property.Key, property.Value);
                }
            }

            return this;
        }

        /// <summary>
        /// Sets the outcome of the operation. The first call wins, so that an early failure isn't overridden by a later generic outcome.
        /// </summary>
        /// <param name="resultCode">Short, fixed result code (e.g. <c>OK</c>, <c>NoDevice</c>).</param>
        /// <param name="success">Whether the operation succeeded. Defaults to <see langword="true"/> only for <see cref="ResultOk"/>.</param>
        public void Complete(string resultCode, bool? success = null)
        {
            if (_resultCode is not null)
            {
                return;
            }

            _resultCode = resultCode;
            _success = success ?? resultCode == ResultOk;
        }

        /// <summary>
        /// Sets a failure outcome and returns the exception, for use in a throw expression:
        /// <c>throw operation.CompleteWith("NoDevice", new DeploymentException(...));</c>
        /// </summary>
        public TException CompleteWith<TException>(string resultCode, TException exception) where TException : Exception
        {
            Complete(resultCode, false);

            return exception;
        }

        /// <summary>
        /// Records the exception as an <c>ExceptionThrown</c> event, correlated with this operation, and sets the result code to <see cref="ResultException"/>.
        /// </summary>
        public void Fail(Exception exception, string stage = null)
        {
            ExtensionTelemetry.TrackException(exception, stage ?? Name);
            Complete(ResultException, false);
        }

        /// <summary>
        /// Stops the operation, which records it.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_activity is null)
            {
                return;
            }

            try
            {
                TelemetrySetup.StopActivity(_activity, _resultCode ?? ResultUnknown, _resultCode is null || _success, _makeCurrent, _previousActivity);
            }
            catch
            {
                // telemetry must never break the extension
            }
        }
    }
}
