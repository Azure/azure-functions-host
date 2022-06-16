using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    public class VariableManager : IVariableManager
    {
        // _variables maps variable name to variable value
        private IDictionary<string, object> _variables;

        // _expressions store registered Expression object
        private IList<Expression> _expressions;

        public VariableManager()
        {
            _variables = new Dictionary<string, object>();
            _expressions = new List<Expression>();
        }

        /// <summary>
        /// Allow an Expression object to subscribe. 
        /// The expression object is first evaluated using the availabe variables.
        /// If it still have unresolved dependency, then add it to the _expression list
        /// 
        /// </summary>
        /// <param name="expression"></param>
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

        public void Clear()
        {
            _variables.Clear();
        }

        // TODO: to be deleted, for debugging
        public void PrintVariables()
        {
            JsonSerializerOptions options = new JsonSerializerOptions() { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter());

            foreach (KeyValuePair<string, object> variable in _variables)
            {
                Console.WriteLine($"Variable = {variable.Key}");
                Console.WriteLine(JsonSerializer.Serialize(variable.Value, options));
            }
        }
    }


}
