// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

/// <summary>
/// The model used for the response message in 'admin/host/restart' API endpoint.
/// This contains all the properties from <see cref="ScriptApplicationHostOptions"/> excluding the <see cref="IServiceProvider"/> property.
/// </summary>
namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class HostRestartResponse
    {
        public bool IsSelfHost { get; set; }

        public string SecretsPath { get; set; }

        public string ScriptPath { get; set; }

        public string LogPath { get; set; }

        public string TestDataPath { get; set; }

        public bool HasParentScope { get; set; }

        public bool IsStandbyConfiguration { get; set; }

        public bool IsFileSystemReadOnly { get; set; }

        public bool IsScmRunFromPackage { get; set; }
    }
}
