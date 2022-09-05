// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization
{
    public interface IRunFromPackageHandler
    {
        Task<bool> MountAzureFileShare(HostAssignmentContext assignmentContext);

        Task<bool> ApplyRunFromPackageContext(RunFromPackageContext pkgContext, string targetPath, bool azureFilesMounted,
            bool throwOnFailure = true);
    }
}