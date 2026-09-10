// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Conditions
{
    /// <summary>
    /// Evaluates a set of <see cref="ICondition"/> instances using AND semantics with
    /// short-circuit on the first failure. Null/empty inputs return true (nothing to gate on).
    /// </summary>
    public static class ConditionEvaluator
    {
        public static bool EvaluateAll(IReadOnlyList<ICondition> conditions, ILogger logger = null)
        {
            if (conditions == null || conditions.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < conditions.Count; i++)
            {
                var condition = conditions[i];
                if (condition == null || !condition.Evaluate())
                {
                    logger?.LogDebug("Condition at index {index} ({type}) did not evaluate to true.", i, condition?.GetType().Name ?? "null");
                    return false;
                }
            }

            return true;
        }
    }
}
