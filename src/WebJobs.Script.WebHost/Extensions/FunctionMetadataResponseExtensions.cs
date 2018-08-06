// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Extensions
{
    public static class FunctionMetadataResponseExtensions
    {
        public static string GetFunctionPath(this FunctionMetadataResponse function, ScriptJobHostOptions config)
            => VirtualFileSystem.VfsUriToFilePath(function.ScriptRootPathHref, config);

        public static string GetFunctionTestDataFilePath(this FunctionMetadataResponse function, ScriptJobHostOptions config)
            => VirtualFileSystem.VfsUriToFilePath(function.TestDataHref, config);
    }
}