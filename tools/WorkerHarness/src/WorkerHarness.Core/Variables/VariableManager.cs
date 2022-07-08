// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.Variables
{
    public class VariableManager : IVariableObservable
    {
        // maps variable name to variable value
        private readonly IDictionary<string, object> _variables;

        // store subscribed Expression object
        private readonly IList<IExpression> _expressions;

        // exception messages
        internal static string DuplicateVariableMessage = "A variable with a name \"{0}\" has already exisited in the Variable dictionary";

        public VariableManager()
        {
            _variables = new Dictionary<string, object>();
            _expressions = new List<IExpression>();
        }

        // constructor for unit testing
        internal VariableManager(IDictionary<string, object> variables, IList<IExpression> expressions)
        {
            _variables = variables;
            _expressions = expressions;
        }

        /// <summary>
        /// Allow an IExpression object to subscribe. 
        /// The expression object is first evaluated using the availabe variables.
        /// If it still have unresolved dependency, keep it in the _expressions list
        /// </summary>
        /// <param name="expression" cref="ExpressionBase"></param>
        public void Subscribe(IExpression expression)
        {
            bool expressionResolved = expression.TryEvaluate(out string? _);
            if (expressionResolved) return;

            foreach (KeyValuePair<string, object> variable in _variables)
            {
                expression.TryResolve(variable.Key, variable.Value);
            }

            expressionResolved = expression.TryEvaluate(out string? _);
            if (!expressionResolved)
            {
                _expressions.Add(expression);
            }
        }

        /// <summary>
        /// Add variable. Notify all subscribed expressions.
        /// </summary>
        /// <param name="variableName" cref="string"></param>
        /// <param name="variableValue" cref="object"></param>
        public void AddVariable(string variableName, object variableValue)
        {
            if (!_variables.TryAdd(variableName, variableValue))
            {
                throw new InvalidDataException(string.Format(DuplicateVariableMessage, variableName));
            }

            HashSet<IExpression> expressionsToRemove = new();
            foreach (IExpression expression in _expressions)
            {
                bool resolved = expression.TryResolve(variableName, variableValue);
                if (resolved)
                {
                    expressionsToRemove.Add(expression);
                }
            }

            foreach (IExpression expression in expressionsToRemove)
            {
                _expressions.Remove(expression);
            }
        }

        /// <summary>
        /// Clear all stored variables and reset the state.
        /// </summary>
        public void Clear()
        {
            _variables.Clear();
            _expressions.Clear();
        }
    }

}
