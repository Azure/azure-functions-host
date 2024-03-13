// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

/// <summary>
/// This is a copy of the <see cref="ScriptApplicationHostOptions"/> excluding the <see cref="IServiceProvider"/> property.
/// The purpose of this class is to provide a subset of the <see cref="ScriptApplicationHostOptions"/> that can be serialized
/// when used in API responses.
/// </summary>
namespace Microsoft.Azure.WebJobs.Script
{
    public class ScriptApplicationHostModel
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
