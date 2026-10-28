//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Application Insights connection string used by the extension telemetry.
    /// </summary>
    internal static class TelemetryConnection
    {
        /// <summary>
        /// The placeholder is replaced by the release pipeline (see azure-pipelines.yml).
        /// While it's not replaced (local and PR builds) telemetry stays disabled.
        /// </summary>
        internal const string ConnectionString = "TELEMETRY_CONNECTION_STRING";

        /// <summary>
        /// Environment variable that, in DEBUG builds only, overrides <see cref="ConnectionString"/>
        /// so that telemetry can be tested against a development Application Insights resource.
        /// </summary>
        internal const string ConnectionStringEnvironmentVariable = "NANOFRAMEWORK_TELEMETRY_CONNECTION_STRING";

        internal static string Get()
        {
#if DEBUG
            string overrideConnectionString = System.Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);

            if (!string.IsNullOrWhiteSpace(overrideConnectionString))
            {
                return overrideConnectionString;
            }
#endif
            return ConnectionString;
        }
    }
}
