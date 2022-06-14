using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    internal class VariableManager
    {
        private IDictionary<string, object?> _variables;

        private IList<Expression> _expressions;

        internal VariableManager()
        {
            _variables = new Dictionary<string, object?>();
            _expressions = new List<Expression>();
        }

        public void Subscribe(Expression expression)
        {
            _expressions.Add(expression);
        }

        /// <summary>
        /// Add variable name and value.
        /// Values should not be of type Expression
        /// </summary>
        /// <param name="variableName"></param>
        /// <param name="variableValue"></param>
        /// <exception cref="InvalidDataException">throw when a variable value is of type Expression</exception>
        public void AddVariable(string variableName, object variableValue)
        {
            if (variableValue is Expression)
            {
                throw new InvalidDataException($"A variable value cannot be a {typeof(Expression)} object");
            }

            // add the name/value pair to _variables
            _variables.Add(variableName, variableValue);

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
    }


}
