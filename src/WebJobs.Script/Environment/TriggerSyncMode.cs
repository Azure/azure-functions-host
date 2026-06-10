// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Controls how the Functions Host propagates sync triggers updates.
    /// Selected at startup via the <see cref="EnvironmentSettingNames.FunctionsSyncTriggersMode"/> environment variable.
    /// </summary>
    internal enum TriggerSyncMode
    {
        /// <summary>
        /// Default behavior. The host POSTs the triggers payload directly to the
        /// platform front end (<c>/operations/settriggers</c>).
        /// </summary>
        FrontEnd = 0,

        /// <summary>
        /// In addition to the default front-end sync, the host fires a best-effort
        /// notification to the in-pod mesh server so the platform can asynchronously
        /// fetch and persist the triggers payload. The mesh notification is
        /// fire-and-forget; failure does not affect the front-end sync result.
        /// </summary>
        Platform = 1,
    }
}
