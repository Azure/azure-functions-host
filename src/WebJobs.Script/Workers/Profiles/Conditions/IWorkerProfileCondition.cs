// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers.Profiles
{
    /// <summary>
    /// Interface for different types of profile conditions
    /// </summary>
    public interface IWorkerProfileCondition
    {
        /// <summary>
        /// Checks if a conditions criteria is being met
        /// </summary>
        bool Evaluate();
    }
}