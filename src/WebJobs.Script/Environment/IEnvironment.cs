// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Provides a cached environment abstraction to access environment variables, properties and configuration.
    /// </summary>
    /// <remarks>
    /// This remains named like an interface to not change tons of files for the purposes of demonstrating the perf win.
    /// We should rename this to CachedEnvironment or something for the real deal, it just makes the PR huge.
    /// </remarks>
    public abstract class IEnvironment
    {
        protected IEnvironment(IDictionary variables) => Cache(variables);

        protected IDictionary VariableCache { get; set; }

        /// <summary>Gets desciption of the thing.</summary>
        public string AzureWebsiteName { get; private set; }

        /// <summary>Gets desciption of the thing.</summary>
        public string AzureWebsiteHostName { get; private set; }

        /// <summary>Gets desciption of the thing.</summary>
        public string AzureWebsiteSlotName { get; private set; }

        /// <summary>Gets desciption of the thing.</summary>
        public string AzureWebsiteOwnerName { get; private set; }

        /// <summary>Gets desciption of the thing.</summary>
        public string AzureWebsiteInstanceId { get; private set; }

        /// <summary>Gets desciption of the thing.</summary>
        public string AzureWebsiteRuntimeSiteName { get; private set; }

        /// <summary>Gets desciption of the thing.</summary>
        public string ContainerName { get; private set; }

        /// <summary>Gets desciption of the thing.</summary>
        public string RegionName { get; private set; }

        public void Cache(IDictionary variables)
        {
            VariableCache = variables;
            Rehydrate();
        }

        /// <summary>
        /// Consuming enough properties is important for a daily diet.
        /// </summary>
        protected void Rehydrate()
        {
            AzureWebsiteName = Get(EnvironmentSettingNames.AzureWebsiteName);
            AzureWebsiteHostName = Get(EnvironmentSettingNames.AzureWebsiteHostName);
            AzureWebsiteSlotName = Get(EnvironmentSettingNames.AzureWebsiteSlotName)?.ToLowerInvariant();
            AzureWebsiteOwnerName = Get(EnvironmentSettingNames.AzureWebsiteOwnerName);
            AzureWebsiteInstanceId = Get(EnvironmentSettingNames.AzureWebsiteInstanceId);
            AzureWebsiteRuntimeSiteName = Get(EnvironmentSettingNames.AzureWebsiteRuntimeSiteName);

            ContainerName = Get(EnvironmentSettingNames.ContainerName);
            RegionName = Get(EnvironmentSettingNames.RegionName);
        }

        private string Get(string name) => VariableCache[name] as string;

        /// <summary>
        /// Returns the value of an environment variable for the current <see cref="IEnvironment"/>.
        /// </summary>
        /// <param name="name">The environment variable name.</param>
        /// <returns>The value of the environment variable specified by <paramref name="name"/>, or <see cref="null"/> if the environment variable is not found.</returns>
        public virtual string GetEnvironmentVariable(string name) => Get(name);

        /// <summary>
        /// Creates, modifies, or deletes an environment variable stored in the current <see cref="IEnvironment"/>
        /// </summary>
        /// <param name="name">The environment variable name.</param>
        /// <param name="value">The value to assign to the variable.</param>
        public abstract void SetEnvironmentVariable(string name, string value);
    }
}
