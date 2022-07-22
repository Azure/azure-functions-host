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
        internal static readonly string InvalidQueryMessage = "The \"{0}\" query is invalid. ";
        internal static readonly string MissingPropertyMessage = "The property \"{0}\" is not present in the queried object. ";
        internal static readonly string InvalidPropertyMessage = "The property {0} is invalid because {1}. ";

        // Exception type
        internal static readonly string QuerySyntaxError = "QuerySyntaxError";
        internal static readonly string MissingPropertyError = "MissingPropertyError";

        /// <summary>
        /// Query an object using the given query
        /// </summary>
        /// <param name="obj" cref="object"></param>
        /// <param name="query" cref="string"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        internal static object Query(this object obj, string query)
        {
            if (!IsQueryValid(query))
            {
                ArgumentException ex = new(string.Format(InvalidQueryMessage, query));
                ex.Data.Add("Type", QuerySyntaxError);
                ex.Data.Add("Data", query);

                throw ex;
            }

            JsonNode jsonNode;
            if (obj is JsonNode node)
            {
                jsonNode = node;
            }
            else
            {
                // convert the obj to a JsonNode object to query
                JsonSerializerOptions options = new();
                options.Converters.Add(new JsonStringEnumConverter());
                byte[] jsonStream = JsonSerializer.SerializeToUtf8Bytes(obj, options);
                jsonNode = JsonNode.Parse(jsonStream)!;
            }

            // find all properties to index into
            string trimmedQuery = Regex.Replace(query, QueryPattern, string.Empty);
            string[] properties = string.IsNullOrEmpty(trimmedQuery) ? Array.Empty<string>() : trimmedQuery.Split(".");

            // recursively index into jsonNode until all properties have been traversed
            try
            {
                JsonNode rawQueryResult = RecursiveIndex(jsonNode, properties);

                object queryResult;
                if (rawQueryResult is JsonValue)
                {
                    queryResult = rawQueryResult.ToString();
                }
                else
                {
                    queryResult = rawQueryResult;
                }

                return queryResult;
            }
            catch (ArgumentException ex)
            {
                ex.Data.Add("Data", query);

                throw ex;
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
                string message = string.Format(MissingPropertyMessage, property);
                ArgumentException ex = new(message);
                ex.Data.Add("Type", MissingPropertyError);

                throw ex;
            }

            Match indexPatternMatch = Regex.Match(property, ArrayIndexPattern);
            bool hasIndexPattern = Regex.IsMatch(property, ArrayIndexPattern);

            if (propertyValue is JsonObject)
            {
                if (hasIndexPattern)
                {
                    string message = string.Format(InvalidPropertyMessage, property, $"{propertyName} is a Json object");
                    ArgumentException ex = new(message);
                    ex.Data.Add("Type", QuerySyntaxError);

                    throw ex;
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
                        string message = string.Format(InvalidPropertyMessage, property, $"{property} is a Json array but the index is not an integer.");
                        ArgumentException ex = new(message);
                        ex.Data.Add("Type", QuerySyntaxError);

                        throw ex;
                    }
                }
                else
                {
                    string message = string.Format(InvalidPropertyMessage, property, $"{property} is a Json array but missing an integer index");
                    ArgumentException ex = new(message);
                    ex.Data.Add("Type", QuerySyntaxError);

                    throw ex;
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
