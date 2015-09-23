// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ScriptHostConfiguration
    {
        /// <summary>
        /// Gets or sets the full path to the application root directory.
        /// </summary>
        public string ApplicationRootPath { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="Assembly"/> of the host
        /// application.
        /// </summary>
        public Assembly HostAssembly { get; set; }
    }
}
