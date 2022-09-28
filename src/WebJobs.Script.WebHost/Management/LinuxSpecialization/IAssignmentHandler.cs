// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization
{
    public interface IAssignmentHandler
    {
        Task<string> ValidateContext(HostAssignmentContext assignmentContext);

        Task<string> SpecializeMSISidecar(HostAssignmentContext context);

        Task<string> Download(HostAssignmentContext context);

        Task ApplyFileSystemChanges(HostAssignmentContext assignmentContext);
    }
}