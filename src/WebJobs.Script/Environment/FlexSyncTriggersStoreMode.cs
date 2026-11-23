// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Where a Flex Consumption app's sync triggers payload is stored, set by the platform through
    /// <see cref="EnvironmentSettingNames.FunctionsFlexSyncTriggersStore"/>.
    /// </summary>
    public enum FlexSyncTriggersStoreMode
    {
        /// <summary>The host publishes the payload to the hosting control plane (SQL) only. The default.</summary>
        Sql,

        /// <summary>The host publishes to SQL and, in parallel, asks the platform to store the payload in Cosmos.</summary>
        SqlAndCosmos,

        /// <summary>The host asks the platform to store the payload in Cosmos on foreground syncs and doesn't publish to SQL; background syncs do nothing.</summary>
        Cosmos,
    }
}
