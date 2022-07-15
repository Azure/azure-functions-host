// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using System.Text.RegularExpressions;

namespace WorkerHarness.Core.Commons
{
    /// <summary>
    /// Contain helper methods to work with variable expressions.
    /// </summary>
    internal static class VariableHelper
    {
        private const string objectVariablePattern = @"(\$\{)([^\{\}]*)(\})\.?";
        private const string stringVariablePattern = @"(@\{)([^\{\}]*)(\})\.?";
        private const string nestedVariablePattern = @"[\$@]\{[\$@]\{.*\}\}\.?";

        // Exception messages
        internal static string MoreThanOneObjectVariableMessage = "Found {0} object variables '${{...}}' in {1}. There can only 1 '${{..}}' per expression";
        internal static string ObjectVariableNotAtZeroIndexMessage = "The object variable '{0}' is at position {1}. It has to be at position 0";
        internal static string NestedVariableMessage = "Found {0} nested variables (e.g., {1}) in {2}. Nested expressions is not supported";

        /// <summary>
        /// Extract all variable names from an expression. Caller must make sure the expression is valid.
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public static IList<string> ExtractVariableNames(string expression)
        {
            HashSet<string> variables = new();

            MatchCollection objectVariableMatches = Regex.Matches(expression, objectVariablePattern);
            foreach (Match match in objectVariableMatches)
            {
                variables.Add(match.Groups[2].Value);
            }

            MatchCollection stringVariableMatches = Regex.Matches(expression, stringVariablePattern);
            foreach (Match match in stringVariableMatches)
            {
                variables.Add(match.Groups[2].Value);
            }

            return variables.ToList();
        }

        /// <summary>
        /// Resolve string variable in a valid expression.
        /// </summary>
        /// <param name="variableName"></param>
        /// <param name="variableValue"></param>
        /// <param name="expression"></param>
        /// <returns></returns>
        public static string ResolveStringVariable(string variableName, string variableValue, string expression)
        {
            string variableNamePattern = @"@\{" + variableName + @"\}";
            string newExpression = Regex.Replace(expression, variableNamePattern, variableValue);

            return newExpression;
        }

        /// <summary>
        /// Resolve object variable in a valid expression.
        /// </summary>
        /// <param name="variableName"></param>
        /// <param name="variableValue"></param>
        /// <param name="expression"></param>
        /// <returns></returns>
        public static string ResolveObjectVariable(string variableName, object? variableValue, string expression)
        {
            if (variableValue == null || Regex.IsMatch(expression, stringVariablePattern))
            {
                return expression;
            }

            string objectNamePattern = @"\$\{" + variableName + @"\}\.?";

            if (Regex.IsMatch(expression, objectNamePattern))
            {
                string query = Regex.Replace(expression, objectNamePattern, "$.");
                object rawQueryResult = variableValue.Query(query);

                string queryResult;
                if (rawQueryResult is string)
                {
                    queryResult = rawQueryResult.ToString() ?? string.Empty;
                }
                else
                {
                    queryResult = JsonSerializer.Serialize(rawQueryResult);
                }

                return queryResult;
            }
            else
            {
                return expression;
            }
            
        }

        /// <summary>
        /// Return true if a valid expression contains variables, false otherwise.
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public static bool ContainVariables(string expression)
        {
            MatchCollection objectVariableMatches = Regex.Matches(expression, objectVariablePattern);
            MatchCollection stringVariableMatches = Regex.Matches(expression, stringVariablePattern);

            return objectVariableMatches.Count > 0 || stringVariableMatches.Count > 0;
        }


        /// <summary>
        /// Validate a variable expression.
        /// Only 1 object variable is allowed at the beginning of the expression.
        /// No limit on the number of string variables.
        /// Nested variables are not allowed.
        /// </summary>
        /// <param name="expression" cref="string"></param>
        /// <exception cref="InvalidDataException">throw when the expression contains invalid variables</exception>
        public static void ValidateVariableExpression(string expression)
        {
            MatchCollection? objectExpressionMatches = Regex.Matches(expression, objectVariablePattern);
            if (objectExpressionMatches.Count > 1)
            {
                throw new InvalidDataException(string.Format(MoreThanOneObjectVariableMessage, objectExpressionMatches.Count, expression));
            }
            if (objectExpressionMatches.Count == 1 && objectExpressionMatches.First().Index != 0)
            {
                throw new InvalidDataException(string.Format(ObjectVariableNotAtZeroIndexMessage, objectExpressionMatches.First().Value, objectExpressionMatches.First().Index));
            }

            MatchCollection nestedExpressionMatches = Regex.Matches(expression, nestedVariablePattern);
            if (nestedExpressionMatches.Count > 0)
            {
                throw new InvalidDataException(string.Format(NestedVariableMessage, nestedExpressionMatches.Count, nestedExpressionMatches.First().Value, expression));
            }
        }
    }
}
