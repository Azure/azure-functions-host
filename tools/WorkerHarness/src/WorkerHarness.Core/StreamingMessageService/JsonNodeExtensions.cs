// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;
using WorkerHarness.Core.Variables;

namespace WorkerHarness.Core.StreamingMessageService
{
    internal static class JsonNodeExtensions
    {
        internal static string VariableCannotBeSolved = "The {0} expression contain a variable that cannot be solved because it is not avaible in the global variables";

        /// <summary>
        /// An extension method to solve any variables in a JsonNode.
        /// </summary>
        /// <param name="node" cref="JsonNode"></param>
        /// <param name="globalVariables" cref="IVariableObservable"></param>
        /// <returns></returns>
        internal static JsonNode SolveVariables(this JsonNode node, IVariableObservable globalVariables)
        {
            return RecursiveSolveVariables(node, globalVariables);
        }

        private static JsonNode RecursiveSolveVariables(JsonNode node, IVariableObservable globalVariables)
        {
            if (node is JsonArray nodeAsJsonArray)
            {
                JsonArray newArray = new();

                IEnumerator<JsonNode?> enumerator = nodeAsJsonArray.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    JsonNode newNode = RecursiveSolveVariables(enumerator.Current!, globalVariables);
                    newArray.Add(newNode);
                }

                return newArray;
            }
            else if (node is JsonObject nodeAsJsonObject)
            {
                JsonObject newObject = new();

                IEnumerator<KeyValuePair<string, JsonNode?>> enumerator = nodeAsJsonObject.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    string propertyName = enumerator.Current.Key;
                    JsonNode propertyValue = enumerator.Current.Value!;
                    newObject[propertyName] = RecursiveSolveVariables(propertyValue, globalVariables);
                }

                return newObject;
            }
            else if (node is JsonValue nodeAsJsonValue)
            {
                string value = nodeAsJsonValue.GetValue<string>();
                IExpression expression = new Expression(value);
                globalVariables.Subscribe(expression);

                bool solved = expression.TryEvaluate(out string? newValue);
                if (solved)
                {
                    return CreateJsonNode(newValue!);
                }
                else
                {
                    string exceptionMessage = string.Format(VariableCannotBeSolved, value);
                    throw new ArgumentException(exceptionMessage);
                }
            }
            else
            {
                return node;
            }
        }

        private static JsonNode CreateJsonNode(string value)
        {
            try
            {
                JsonNode node = JsonNode.Parse(value)!;

                return node;
            }
            catch (JsonException)
            {
                return JsonValue.Create(value)!;
            }
        }

    }
}
