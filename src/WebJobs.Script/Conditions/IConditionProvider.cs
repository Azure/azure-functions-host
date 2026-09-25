// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Conditions
{
    /// <summary>
    /// Factory that turns a <see cref="ConditionDescriptor"/> into an evaluable
    /// <see cref="ICondition"/>. Implementations choose their own error-handling policy
    /// for unknown condition types (return false vs. return a <see cref="FalseCondition"/>).
    /// </summary>
    public interface IConditionProvider
    {
        /// <summary>
        /// Attempts to construct a condition from the descriptor.
        /// Returns true and assigns <paramref name="condition"/> when successful; otherwise
        /// returns false. A provider that wants to fail-safe can return true with a
        /// <see cref="FalseCondition"/> instead of returning false.
        /// </summary>
        bool TryCreateCondition(ConditionDescriptor descriptor, out ICondition condition);
    }
}
