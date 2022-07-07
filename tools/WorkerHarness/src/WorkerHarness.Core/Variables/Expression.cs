// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core
{
    /// <summary>
    /// Defines an expression
    /// </summary>
    public abstract class Expression
    {
        /// <summary>
        /// Inheriting class must implement this method to become an Expression
        /// </summary>
        public abstract void ConstructExpression();

        // the value of the expression; could contain an object variable name ${...} and several string variable names @{...}
        private string _expression = string.Empty;

        // true if all variables within _expression has been resolve
        private bool _resolved = false;

        public bool Resolved => _resolved;

        // contains variable dependencies in an expression
        private IList<string> _dependencies = new List<string>();

        // an object variable that the expression may depend on.
        private object? _objectVariable;

        /// <summary>
        /// Set a given string to be an Expression
        /// </summary>
        /// <param name="expression"></param>
        protected void SetExpression(string expression)
        {
            _expression = expression;

            _dependencies = VariableHelper.ExtractVariableNames(_expression);

            _resolved = !_dependencies.Any();
        }

        /// <summary>
        /// Update the expression with the given variable value
        /// </summary>
        /// <param name="variableName" cref="string">variable name</param>
        /// <param name="variableValue" cref="string">variable value</param>
        public bool TryResolve(string variableName, object variableValue)
        {
            // update the expression with the variable 
            if (_dependencies.Contains(variableName))
            {
                if (variableValue is string variableValueInString) // if the variable is a string variable, update the expression
                {
                    _expression = VariableHelper.ResolveStringVariable(variableName, variableValueInString, _expression);
                }
                else // if the variable is an object variable, buffer it
                {
                    _objectVariable = variableValue;
                }

                // attemp to resolve the object variable in _expression
                _expression = VariableHelper.ResolveObjectVariable(variableName, _objectVariable, _expression);

                // _resolved is true if the expression contains no variables
                _resolved = !VariableHelper.ContainVariables(_expression);

                // discard the _objectVariable if _resolved
                _objectVariable = _resolved ? null : _objectVariable;
            }

            return _resolved;
        }

        /// <summary>
        /// Output the fully evaluated expression if all dependencies are resolved.
        /// </summary>
        /// <param name="value"></param>
        /// <returns>true if all dependencies are resolved, false otherwise</returns>
        public bool TryEvaluate(out string? value)
        {
            if (_resolved)
            {
                value = _expression;
                return true;
            }
            else
            {
                value = null;
                return false;
            }
        }
    }
}
