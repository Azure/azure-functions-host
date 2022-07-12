// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WorkerHarness.Core.Commons
{
    /// <summary>
    /// Contains extension method to query the property of an object.
    /// </summary>
    internal static class ObjectQuery
    {
        private static readonly string QueryPattern = @"^\$\.";
        private static readonly string ArrayIndexPattern = @"\[[0-9]+\]$";

        // Exception messages
        internal static readonly string InvalidQueryMessage = "The \"{0}\" query is invalid";
        internal static readonly string MissingPropertyMessage = "The property \"{0}\" is not present in the queried object";
        internal static readonly string InvalidPropertyMessage = "the property {0} is invalid because {1}";

        /// <summary>
        /// Query an object using the given query
        /// </summary>
        /// <param name="obj" cref="object"></param>
        /// <param name="query" cref="string"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        internal static string Query(this object obj, string query)
        {
            if (!IsQueryValid(query))
            {
                throw new ArgumentException(string.Format(InvalidQueryMessage, query));
            }

            // convert the obj to a JsonNode object to query
            JsonSerializerOptions options = new () { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter());
            byte[] jsonStream = JsonSerializer.SerializeToUtf8Bytes(obj, options);
            JsonNode jsonNode = JsonNode.Parse(jsonStream)!;

            // find all properties to index into
            string[] properties = Regex.Replace(query, QueryPattern, string.Empty).Split(".");

            // recursively index into jsonNode until all properties have been traversed
            try
            {
                JsonNode queryResult = RecursiveIndex(jsonNode, properties);
                string value = queryResult is JsonValue ? queryResult.ToString() : JsonSerializer.Serialize(queryResult);

                return value;
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException(ex.Message);
            }

        }

        private static JsonNode RecursiveIndex(JsonNode jsonNode, string[] properties, int index=0)
        {
            if (index == properties.Length)
            {
                return jsonNode;
            }

            string property = properties[index];
            string propertyName = Regex.Replace(property, ArrayIndexPattern, string.Empty);
            JsonNode? propertyValue = jsonNode[propertyName];

            if (propertyValue == null)
            {
                throw new ArgumentException(string.Format(MissingPropertyMessage, property));
            }

            Match indexPatternMatch = Regex.Match(property, ArrayIndexPattern);
            bool hasIndexPattern = Regex.IsMatch(property, ArrayIndexPattern);

            if (propertyValue is JsonObject)
            {
                if (hasIndexPattern)
                {
                    string exceptionMessage = string.Format(InvalidPropertyMessage, property, $"{propertyName} is a Json object");
                    throw new ArgumentException(exceptionMessage);
                }

                return RecursiveIndex(propertyValue, properties, index + 1);
            }
            else if (propertyValue is JsonArray)
            {
                if (hasIndexPattern)
                {
                    try
                    {
                        int length = indexPatternMatch.Length;
                        int arrayIndex = int.Parse(indexPatternMatch.Value.Substring(1, length - 2));

                        return RecursiveIndex(propertyValue[arrayIndex]!, properties, index + 1);
                    }
                    catch (FormatException)
                    {
                        string exceptionMessage = string.Format(InvalidPropertyMessage, property, $"{property} is a Json array but the index is not an integer.");
                        throw new ArgumentException(exceptionMessage);
                    }
                }
                else
                {
                    string exceptionMessage = string.Format(InvalidPropertyMessage, property, $"{property} is a Json array but missing an integer index");
                    throw new ArgumentException(exceptionMessage);
                }
            }
            else
            {
                return RecursiveIndex(propertyValue, properties, index + 1);
            }
        }

        private static bool IsQueryValid(string query)
        {
            return Regex.IsMatch(query, QueryPattern);
        }
    }
}
