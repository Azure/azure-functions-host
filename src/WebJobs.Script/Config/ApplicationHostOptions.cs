// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

// TODO: documentation. This is a copy of the ScriptApplicationHostOptions class from the Microsoft.Azure.WebJobs.Script namespace.
// This is returned when admin/host/restart is called
namespace Microsoft.Azure.WebJobs.Script
{
    public class ApplicationHostOptions // TODO: name this more specifically to what the restart API needs?
    {
        public bool IsSelfHost { get; set; }

        public string SecretsPath { get; set; }

        public string ScriptPath { get; set; }

        public string LogPath { get; set; }

        public string TestDataPath { get; set; }

        public bool HasParentScope { get; set; }

        public bool IsStandbyConfiguration { get; internal set; }

        public bool IsFileSystemReadOnly { get; set; }

        public bool IsScmRunFromPackage { get; set; }
    }
}
