using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WorkerHarness.Core
{
    /// <summary>
    /// Many helper methods to validate and resolve variable expressions.
    /// </summary>
    internal static class VariableHelper
    {
        private const string defaultObjectVariablePattern = @"\$\.";
        private const string objectVariablePattern = @"(\$\{)([^\{\}]+)(\}\.*)";
        private const string nestedVariablePattern = @"[\$@]\{[\$@]\{.+\}\}";
        private const string stringVariablePattern = @"(@\{)([^\{\}]+)(\})";

        /// <summary>
        /// Extract all variable names from an expression. Caller must make sure the expression is valid.
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public static IList<string> ExtractVariableNames(string expression)
        {
            IList<string> variables = new List<string>();

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

            return variables;
        }

        /// <summary>
        /// Replace any occurent of '@{variableName}' in 'expression' with 'variableValue
        /// </summary>
        /// <param name="variableName"></param>
        /// <param name="variableValue"></param>
        /// <param name="expression"></param>
        /// <returns></returns>
        public static string ResolveStringVariable(string variableName, string variableValue, string expression)
        {
            string variableNamePattern = "@{" + variableName + "}";
            string newExpression = Regex.Replace(expression, variableNamePattern, variableValue);

            return newExpression;
        }

        /// <summary>
        /// Given a valid 'expression' and a variable name and value, resolve the expression
        /// Caller must make sure that the 'expression' is valid and all string variables have been resolved before calling this method.
        /// E.g., ${variableName}.Property1.Subproperty
        /// 
        /// </summary>
        /// <param name="variableName">Name of the variable</param>
        /// <param name="variableValue">Value of the variable</param>
        /// <param name="expression">An valid expression that contains the variable name</param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static string ResolveObjectVariable(string variableName, object? variableValue, string expression)
        {
            if (variableValue == null)
            {
                return expression;
            }

            string jsonString = JsonSerializer.Serialize(variableValue);
            JsonNode jsonNode = JsonNode.Parse(jsonString) ?? throw new InvalidOperationException($"Failed to convert a {typeof(string)} object to a {typeof(JsonNode)} object");
            string[] properties = Regex.Replace(expression, objectVariablePattern, string.Empty).Split(".");

            // recursively index into jsonNode until all properties have been traversed
            object evaluatedResult = RecursiveIndex(jsonNode, properties, 0);

            //return evaluatedResult as a Json string
            return evaluatedResult.ToString() ?? JsonSerializer.Serialize(evaluatedResult);
        }

        /// <summary>
        /// Return true if the expression contains variables, false otherwise.
        /// Assuming the expression is valid.
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public static bool ContainVariables(string expression)
        {
            MatchCollection objectVariableMatches = Regex.Matches(expression, objectVariablePattern);
            MatchCollection stringVariableMatches = Regex.Matches(expression, stringVariablePattern);

            return objectVariableMatches.Count > 0 || stringVariableMatches.Count > 0;
        }


        public static void ResolveVariableMap(IDictionary<string, string>? setVariables, string defaultObjectName, object defaultObjectValue)
        {
            if (setVariables == null)
            {
                return;
            }

            IDictionary<string, object> variableRegistry = new Dictionary<string, object>
            {
                { defaultObjectName, defaultObjectValue }
            };

            foreach (KeyValuePair<string, string> variable in setVariables)
            {
                // first, change the default variable ($.) if any to ${defaultObjectName}.
                string variableExpression = UpdateSingleDefaultVariableExpression(variable.Value, defaultObjectName);
                object variableValue = ResolveSingleVariableExpression(variableExpression, variableRegistry);
                variableRegistry.Add(variable.Key, variableValue);
                setVariables[variable.Key] = variableValue.ToString() ?? JsonSerializer.Serialize(variableValue);
            }
        }

        /// <summary>
        /// Update the default variable expression '$.' that lacks object name.
        /// E.g., variableMap['variable_1'] = "$.Property.Subproperty" will be updated to 
        /// variableMap['variable_1'] = "${objectName}.Property.Subproperty".
        /// </summary>
        /// <param name="variableMap" cref="IDictionary{string, string}">maps a variable name to its value or expression</param>
        /// <param name="objectName" cref="string">the object name to update the default expression</param>
        public static void UpdateDefaultVariableExpressions(IDictionary<string, string> variableMap, string objectName)
        {
            foreach (KeyValuePair<string, string> variable in variableMap)
            {
                variableMap[variable.Key] = UpdateSingleDefaultVariableExpression(variable.Value, objectName);
            }
        }

        /// <summary>
        /// Replace the default object expression '$.' with '${objectName}'.
        /// </summary>
        /// <param name="expression" cref="string">a string that may contain a default object expression</param>
        /// <param name="objectName" cref="string">the object name to replace the default object expression</param>
        /// <returns></returns>
        public static string UpdateSingleDefaultVariableExpression(string expression, string objectName)
        {
            ValidateDefaultVariableExpression(expression);

            string replacement = @"${" + objectName + "}.";
            string newExpression = Regex.Replace(expression, defaultObjectVariablePattern, replacement);

            return newExpression;
        }

        /// <summary>
        /// Validates the requirement that an expression contains one and only one object variable.
        /// </summary>
        /// <param name="expression" cref="string">an expression that may contain variables</param>
        /// <exception cref="InvalidDataException">throws when there are more than one object variable or the object variable is not at the beginning of an expression</exception>
        private static void ValidateDefaultVariableExpression(string expression)
        {
            MatchCollection matches = Regex.Matches(expression, defaultObjectVariablePattern);
            if (matches.Count > 1)
            {
                throw new InvalidDataException($"Found {matches.Count} default object variables '$.' in {expression}. There can only 1 '$.' per expression");
            }
            if (matches.Count == 1 && matches.First().Index != 0)
            {
                throw new InvalidDataException($"The default object variable '$.' is at position {matches.First().Index}. It has to be at position 0");
            }

        }

        /// <summary>
        /// Resolve all variable expressions in 'variables' dictionary.
        /// Variable expressions contain string variable @{...} or object variable ${...}
        /// Look up the variableRegistry dictionary to resolve these variable expressions.
        /// </summary>
        /// <param name="variables" cref="IDictionary{string, string}">maps a variable name to a variable expression</param>
        /// <param name="variableRegistry" cref="IDictionary{string, object}">maps a variable name to an object</param>
        public static IDictionary<string, object> ResolveVariableExpressions(IDictionary<string, string> variables, IDictionary<string, object> variableRegistry)
        {
            IDictionary<string, object> resolvedVariables = new Dictionary<string, object>();

            foreach (KeyValuePair<string, string> variable in variables)
            {
                // resolve the variable expressions
                resolvedVariables[variable.Key] = ResolveSingleVariableExpression(variable.Value, variableRegistry);
            }

            return resolvedVariables;
        }

        /// <summary>
        /// There are 2 types of variable expressions:
        /// - object variable: ${...}
        /// - string variable: @{...}
        /// 
        /// Each variable expression contains ONE and ONLY ONE object variable to avoid complexity.
        /// The object variable should be at the beginning of each expression.
        /// Some invalid object variables are:
        ///     - ${object1}.${object2} : there are 2 object variables here
        ///     - ABC.${object_name}.DEF: the object variable is not at the beginning
        ///     
        /// There is no limit to the number of string variables.
        ///     - ${object1}.@{property_name}.@{subproperty_name} is valid.
        /// 
        /// Nested expressions are not allowed to avoid complexity.
        /// E.g. ${${object}}, ${@{object}}, @{${object}} are invalid
        /// 
        /// </summary>
        /// <param name="expression" cref="string">an expression that contains either object variable or string variable</param>
        /// <exception cref="InvalidDataException">throw when the expression contains invalid variables</exception>
        private static void ValidateVariableExpression(string expression)
        {
            MatchCollection? objectExpressionMatches = Regex.Matches(expression, objectVariablePattern);
            if (objectExpressionMatches.Count > 1)
            {
                throw new InvalidDataException($"Found {objectExpressionMatches.Count} object variables '${{...}}' in {expression}. There can only 1 '${{...}}' per expression");
            }
            if (objectExpressionMatches.Count == 1 && objectExpressionMatches.First().Index != 0)
            {
                throw new InvalidDataException($"The object variable '{objectExpressionMatches.First().Value}' is at position {objectExpressionMatches.First().Index}. It has to be at position 0");
            }

            MatchCollection nestedExpressionMatches = Regex.Matches(expression, nestedVariablePattern);
            if (nestedExpressionMatches.Count > 0)
            {
                throw new InvalidDataException($"Found {nestedExpressionMatches.Count} nested variables (e.g., {nestedExpressionMatches.First().Value}) in {expression}. Nested expressions is not supported");
            }
        }

        /// <summary>
        /// Resolve a single expression.
        /// </summary>
        /// <param name="expression" cref="string">a string that may contain an object variable or several string variables.</param>
        /// <param name="variableRegistry" cref="IDictionary{string, object}">map a variable name to its value</param>
        /// <returns cref="object">the evaluated value of the given expression</returns>
        /// <exception cref="InvalidDataException">throw when the variable name does not exist in the variableRegistry dictionary</exception>
        /// <exception cref="InvalidOperationException">throw when the system fails to parse a json string to a JsonNode object</exception>
        public static object ResolveSingleVariableExpression(string expression, IDictionary<string, object> variableRegistry)
        {
            // Validate if the expression is valid
            ValidateVariableExpression(expression);

            // Resolve all the string variables in expression
            var parsedExpression = Regex.Replace(expression, stringVariablePattern, match => SubstituteStringVariable(match, variableRegistry));
            // Resolve the object variable if any
            Match match = Regex.Match(parsedExpression, objectVariablePattern);
            if (!match.Success)
            {
                return parsedExpression;
            }
            else
            {
                string objectName = match.Groups[2].Value;

                if (!variableRegistry.TryGetValue(objectName, out var obj))
                {
                    throw new InvalidDataException($"The variable {objectName} does not exist in the dictionary {nameof(variableRegistry)}");
                }
                else
                {
                    // convert the 'obj' to a JsonNode object to traverse
                    string jsonString = JsonSerializer.Serialize(obj);
                    JsonNode jsonNode = JsonNode.Parse(jsonString) ?? throw new InvalidOperationException($"Failed to convert a {typeof(string)} object to a {typeof(JsonNode)} object");
                    string[] properties = Regex.Replace(expression, objectVariablePattern, string.Empty).Split(".");
                    // recursively index into jsonNode until all properties have been traversed
                    return RecursiveIndex(jsonNode, properties, 0);
                }
            }
        }

        /// <summary>
        /// Return the value after traversing all the properties
        /// </summary>
        /// <param name="jsonNode" cref="JsonNode">a root node object to traverse</param>
        /// <param name="properties" cref="string[]">an array of properties</param>
        /// <param name="index" cref="int">an integer to index into the properties array</param>
        /// <returns cref="object"></returns>
        /// <exception cref="MissingFieldException">throws when a property in the properties array is not found inside the root node</exception>
        public static JsonNode RecursiveIndex(JsonNode jsonNode, string[] properties, int index)
        {
            if (index == properties.Length)
            {
                return jsonNode;
            }
            else
            {
                string property = properties[index];
                JsonNode? propertyValue = jsonNode[property];
                if (propertyValue != null)
                {
                    return RecursiveIndex(propertyValue, properties, index + 1);
                }
                else
                {
                    throw new MissingFieldException($"The property {property} is not present in the {typeof(JsonNode)} object");
                }
            }
        }

        /// <summary>
        /// Return a string value of a string variable after being matched.
        /// </summary>
        /// <param name="match" cref="Match">a Match objec that contains the string variable name</param>
        /// <param name="globalVariables" cref="IDictionary{string, object}">maps a variable name to its value</param>
        /// <returns cref="string"></returns>
        /// <exception cref="InvalidDataException">throws when the string variable name does not exist in the variableRegistry dictionary</exception>
        private static string SubstituteStringVariable(Match match, IDictionary<string, object> variableRegistry)
        {
            // if the matched string variable is @{property},
            // then the match.Groups = {'@{property}', '@{', 'property', '}'}
            string variableName = match.Groups[2].Value;
            if (!variableRegistry.TryGetValue(variableName, out object? variableValue))
            {
                throw new InvalidDataException($"The variable {nameof(variableName)} does not exist in the dictionary {nameof(variableRegistry)}");
            }
            else
            {
                return variableValue as String ?? throw new InvalidDataException($"The dictionary {nameof(variableRegistry)} does not contain a string value for the variable {nameof(variableName)}");
            }
        }

        /// <summary>
        /// Update the target dictionary with the source dictionary
        /// </summary>
        /// <param name="target" cref="IDictionary{string, object}"></param>
        /// <param name="source" cref="IDictionary{string, object}"></param>
        public static void UpdateMap(IDictionary<string, object> target, IDictionary<string, object> source)
        {
            foreach (KeyValuePair<string, object> pair in source)
            {
                target[pair.Key] = pair.Value;
            }
        }

        /// <summary>
        /// Update the target dictionary with the source dictionary
        /// </summary>
        /// <param name="target" cref="IDictionary{string, object}"></param>
        /// <param name="source" cref="IDictionary{string, string}"></param>
        public static void UpdateMap(IDictionary<string, object> target, IDictionary<string, string> source)
        {
            foreach (KeyValuePair<string, string> pair in source)
            {
                target[pair.Key] = pair.Value;
            }
        }
    }
}
