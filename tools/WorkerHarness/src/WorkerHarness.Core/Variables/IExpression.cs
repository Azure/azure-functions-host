// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.Variables
{
    public interface IExpression
    {
        /// <summary>
        /// Resolve an IExpression with a variable
        /// </summary>
        /// <param name="variableName"></param>
        /// <param name="variableValue"></param>
        /// <returns>true if an IExpression does not contain any variables</returns>
        public bool TryResolve(string variableName, object variableValue);

        /// <summary>
        /// Evaluate an IExpression
        /// </summary>
        /// <param name="value">value of the resolved expression, null if not resolved</param>
        /// <returns>true if an IExpression is fully resolved</returns>
        public bool TryEvaluate(out string? value);
    }
}
