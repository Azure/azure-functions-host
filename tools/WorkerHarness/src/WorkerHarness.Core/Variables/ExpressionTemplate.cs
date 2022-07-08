// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core.Variables
{
    /// <summary>
    /// Defines an expression
    /// </summary>
    public abstract class ExpressionTemplate
    {
        /// <summary>
        /// Inheriting class must implement this method. Inside the method, call SetExpression().
        /// </summary>
        public abstract void ConstructExpression();

        // the value of the expression; could contain an object variable name ${...} and several string variable names @{...}
        private string _expression = string.Empty;
        public string Expression => _expression;

        // true if all variables within _expression has been resolve
        private bool _resolved = false;
        public bool Resolved => _resolved;

        // contains variable dependencies in an expression
        private IList<string> _dependencies = new List<string>();
        public IList<string> Dependencies => _dependencies;

        // an object variable that the expression may depend on.
        private KeyValuePair<string, object>? _objectVariable;

        /// <summary>
        /// Set a given string to be an Expression
        /// </summary>
        /// <param name="expression"></param>
        protected internal void SetExpression(string expression)
        {
            try
            {
                VariableHelper.ValidateVariableExpression(expression);
            }
            catch (InvalidDataException ex)
            {
                throw new ArgumentException(string.Concat($"Invalid expression: {expression}. ", ex.Message));
            }

            _expression = expression;
            _dependencies = VariableHelper.ExtractVariableNames(_expression);
            _resolved = !_dependencies.Any();
        }

        /// <summary>
        /// Update the expression with the given variable value
        /// </summary>
        /// <param name="variableName" cref="string">variable name</param>
        /// <param name="variableValue" cref="string">variable value</param>
        /// <returns>true if the expression is fully resolved</returns>
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
                    _objectVariable = new(variableName, variableValue);
                }

                // attemp to resolve the object variable in _expression
                _expression = VariableHelper.ResolveObjectVariable(_objectVariable?.Key ?? string.Empty, _objectVariable?.Value, _expression);

                // _resolved is true if the expression contains no variables
                _resolved = !VariableHelper.ContainVariables(_expression);

                // discard the _objectVariable if _resolved
                _objectVariable = _resolved ? null : _objectVariable;

                // remove variableName from _dependencies
                _dependencies.Remove(variableName);
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
