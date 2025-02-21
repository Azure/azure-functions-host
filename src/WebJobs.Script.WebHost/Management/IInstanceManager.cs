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

        Task<string> ValidateContext(HostAssignmentContext assignmentContext);

        /// <summary>
        /// AssignInstanceAsync will asynchronously assign an instance.
        /// </summary>
        /// <param name="assignmentContext">Takes in a <see cref="HostAssignmentContext"/> which will be applied to an assigned instance.</param>
        /// <returns>Returns true if assiging an instance succeeds, false otherwise.</returns>
        Task<bool> AssignInstanceAsync(HostAssignmentContext assignmentContext);

        /// <summary>
        /// StartAssinment will validate the current environment then assign an instance in a "fire and forget" pattern. Should be used synchronously.
        /// </summary>
        /// <param name="assignmentContext">Takes in a <see cref="HostAssignmentContext"/> which will be applied to an assigned instance</param>
        /// <returns>Returns true if environment validation succeeds, false otherwise.</returns>
        bool StartAssignment(HostAssignmentContext assignmentContext);

        Task<string> SpecializeMSISidecar(HostAssignmentContext assignmentContext);
    }
}
