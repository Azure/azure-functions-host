// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    /// <summary>
    /// Defines a mechanism for resolving the functions worker runtime value for the current context.
    /// </summary>
    public interface IWorkerRuntimeResolver
    {
        /// <summary>
        /// Gets the functions worker runtime value.
        /// </summary>
        /// <param name="defaultValue">The value to return if the worker runtime is not set. If null, the method returns null when no worker
        /// runtime is configured.</param>
        /// <returns>A string containing the worker runtime identifier, or the specified default value if no worker runtime is
        /// set. Returns null if both the worker runtime and <paramref name="defaultValue"/> are not specified.</returns>
        string GetWorkerRuntime(string defaultValue = null);
    }
}
