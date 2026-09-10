// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Conditions
{
    /// <summary>
    /// A condition whose <see cref="Evaluate"/> result is used to decide whether a
    /// requirements-gated feature (e.g. a worker profile or an extension bundle)
    /// should be applied.
    /// </summary>
    public interface ICondition
    {
        /// <summary>
        /// Evaluates the condition. Returns false for malformed or unresolvable conditions
        /// so that callers skip the gated feature instead of throwing.
        /// </summary>
        bool Evaluate();
    }
}
