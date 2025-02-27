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
        /// Asynchronously assigns a host instance.
        /// </summary>
        /// <param name="assignmentContext">The <see cref="HostAssignmentContext"/> that will be applied to the instance being assigned to the application.</param>
        /// <returns><see langword="true"/> if environment validation succeeds; otherwise <see langword="false"/>.</returns>
        Task<bool> AssignInstanceAsync(HostAssignmentContext assignmentContext);

        /// <summary>
        /// Validates the assignment context and begins the assignment process in a "fire and forget" pattern.
        /// </summary>
        /// <param name="assignmentContext">The <see cref="HostAssignmentContext"/> that will be applied to the instance being assigned to the application.</param>
        /// <returns><see langword="true"/> if environment validation succeeds; otherwise <see langword="false"/>.</returns>
        bool StartAssignment(HostAssignmentContext assignmentContext);

        Task<string> SpecializeMSISidecar(HostAssignmentContext assignmentContext);
    }
}
