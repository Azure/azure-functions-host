// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface IScriptWebHostEnvironment
    {
        /// <summary>
        /// Gets a value indicating whether requests should be delayed.
        /// </summary>
        bool DelayRequestsEnabled { get; }

        /// <summary>
        /// Gets a <see cref="Task"> that will complete when the requests no longer need to be delayed.
        /// </summary>
        Task DelayCompletionTask { get; }

        /// <summary>
        /// Gets a value indicating whether the current environment is in standby mode.
        /// </summary>
        bool InStandbyMode { get; }

        /// <summary>
        /// Flags that requests under this environment should be delayed.
        /// </summary>
        void DelayRequests();

        /// <summary>
        /// Flags that requests under this environment should be resumed.
        /// </summary>
        void ResumeRequests();

        /// <summary>
        /// Flags the current environment as ready and specialized.
        /// This sets <see cref="EnvironmentSettingNames.AzureWebsitePlaceholderMode"/> to "0"
        /// and <see cref="EnvironmentSettingNames.AzureWebsiteContainerReady"/> to "1" against
        /// the current environment.
        /// </summary>
        void FlagAsSpecializedAndReady();
    }
}
