// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script
{
    public class ScriptApplicationHostOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether the host is running
        /// outside of the normal Azure hosting environment. E.g. when running
        /// locally or via CLI.
        /// </summary>
        public bool IsSelfHost { get; set; }

        public string SecretsPath { get; set; }

        public string ScriptPath { get; set; }

        public string LogPath { get; set; }

        public string TestDataPath { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the ScriptHost is running inside of a WebHost. When true,
        /// a set of common services will not be registered as they are supplied from the parent WebHost.
        /// </summary>
        public bool HasParentScope { get; set; }
    }
}