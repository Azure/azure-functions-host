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

        // Exception messages
        internal static readonly string InvalidQueryMessage = "The \"{0}\" query is invalid";
        internal static readonly string MissingPropertyMessage = "The property \"{0}\" is not present in the queried object";


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
            catch (MissingFieldException ex)
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
            JsonNode? propertyValue = jsonNode[property];
            if (propertyValue != null)
            {
                return RecursiveIndex(propertyValue, properties, index + 1);
            }
            else
            {
                throw new MissingFieldException(string.Format(MissingPropertyMessage, property));
            }
        }

        private static bool IsQueryValid(string query)
        {
            return Regex.IsMatch(query, QueryPattern);
        }
    }
}
