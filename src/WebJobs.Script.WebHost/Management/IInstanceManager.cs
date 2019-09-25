// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public interface IInstanceManager
    {
        IDictionary<string, string> GetInstanceInfo();

        Task<string> ValidateContext(HostAssignmentContext assignmentContext, bool isWarmup);

        bool StartAssignment(HostAssignmentContext assignmentContext, bool isWarmup);

        Task<string> SpecializeMSISidecar(HostAssignmentContext assignmentContext, bool isWarmup);
    }
}
