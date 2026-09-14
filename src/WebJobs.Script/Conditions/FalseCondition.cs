// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Conditions
{
    /// <summary>
    /// Condition that always evaluates to false. Used as the fail-safe fallback when a
    /// descriptor cannot be resolved to a real condition.
    /// </summary>
    public sealed class FalseCondition : ICondition
    {
        public bool Evaluate() => false;
    }
}
