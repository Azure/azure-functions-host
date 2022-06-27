// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core
{
    public class VariableManager : IVariableObservable
    {
        // maps variable name to variable value
        private readonly IDictionary<string, object> _variables;

        // _expressions store subscribed Expression object
        private readonly IList<Expression> _expressions;

        public VariableManager()
        {
            _variables = new Dictionary<string, object>();
            _expressions = new List<Expression>();
        }

        /// <summary>
        /// Allow an Expression object to subscribe. 
        /// The expression object is first evaluated using the availabe variables.
        /// If it still have unresolved dependency, then add it to the _expression list
        /// </summary>
        /// <param name="expression" cref="Expression"></param>
        public void Subscribe(Expression expression)
        {
            // if expression has dependency that is available, resolve it immediately
            foreach (KeyValuePair<string, object> variable in _variables)
            {
                expression.TryResolve(variable.Key, variable.Value);
            }

            // if expression still has dependency, add it the _expressions list
            if (!expression.Resolved)
            {
                _expressions.Add(expression);
            }
        }

        /// <summary>
        /// Add variable name and value.
        /// All subscribed expressions will be notified and evaluated.
        /// </summary>
        /// <param name="variableName" cref="string"></param>
        /// <param name="variableValue" cref="object"></param>
        public void AddVariable(string variableName, object variableValue)
        {
            // add the name/value pair to _variables
            if (!_variables.TryAdd(variableName, variableValue))
            {
                throw new InvalidOperationException($"A variable with a name \"{variableName}\" has already exisited in the Global Variable dictionary");
            } 

            // update all the expressions that may depend on this variable
            foreach (Expression expression in _expressions)
            {
                bool resolved = expression.TryResolve(variableName, variableValue);
                // if the expression is resolved, removed it from the _expressions list
                if (resolved)
                {
                    _expressions.Remove(expression);
                }
            }
        }

        /// <summary>
        /// Clear all stored variables and reset the state
        /// </summary>
        public void Clear()
        {
            _variables.Clear();
        }
    }

}
