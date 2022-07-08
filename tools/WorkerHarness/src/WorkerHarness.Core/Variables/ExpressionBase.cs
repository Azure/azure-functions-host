// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core.Variables
{
    /// <summary>
    /// a base implementation of IExpression
    /// </summary>
    public abstract class ExpressionBase : IExpression
    {
        /// <summary>
        ///  Call SetExpression(expression) when implementing this method.
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
            if (_dependencies.Contains(variableName))
            {
                if (variableValue is string variableValueInString)
                {
                    _expression = VariableHelper.ResolveStringVariable(variableName, variableValueInString, _expression);
                }
                else
                {
                    _objectVariable = new(variableName, variableValue);
                }

                _expression = VariableHelper.ResolveObjectVariable(_objectVariable?.Key ?? string.Empty, _objectVariable?.Value, _expression);

                _resolved = !VariableHelper.ContainVariables(_expression);

                _objectVariable = _resolved ? null : _objectVariable;

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
