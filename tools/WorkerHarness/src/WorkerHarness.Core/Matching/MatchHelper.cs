using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    internal static class MatchHelper
    {
        private const string objectVariablePattern = @"^(((\$\{)([^\{\}]+)(\}\.*))|(\$\.))";

        /// <summary>
        /// Given an object and a matching criteria, validate whether the object fulfills the matching criteria
        /// A matching criteria includes a query and an expected expression that has been resolved (meaning there is no more variables in the expression)
        /// A query specify a string value to find inside an object
        /// If the string value matches the expected value, then the match succeeds
        /// E.g., query = "{object}.A.B" assumes that the object has the following structure:
        ///     object: { A: { B: "abc", }, }
        ///     The method will query the object for the string "abc"
        ///     If "abc" == the expected value, match succeeds
        ///           
        /// </summary>
        /// <param name="match"></param>
        /// <param name="actual"></param>
        /// <returns></returns>
        internal static bool Matched(MatchingContext match, object source)
        {
            JsonSerializerOptions options = new();
            options.Converters.Add(new JsonStringEnumConverter());

            //if (string.IsNullOrEmpty(match.Query))
            //{
            //    throw new MissingFieldException("The query field is null or empty. A query is needed to do matching");
            //}

            //if (match.ExpectedExpression == null)
            //{
            //    throw new MissingFieldException("The expected expression is null or empty. An expected expression is needed to do matching");
            //}

            // find all properties to index in match.query
            string replacement = Regex.Replace(match.Query, objectVariablePattern, string.Empty);
            string[] properties = replacement.Split(".");
            // convert the actual object to a JsonNode
            string jsonString = JsonSerializer.Serialize(source, options);
            JsonNode jsonNode = JsonNode.Parse(jsonString) ?? throw new InvalidOperationException($"Unable to convert a string to a JsonNode object");
            // recursively index into jsonNode to find the actual string data
            string actual = VariableHelper.RecursiveIndex(jsonNode, properties, 0).ToString() ??
                throw new InvalidDataException($"Unable to find a string value for the query {match.Query}");
            // convert the expected value to a JsonNode
            if (!match.TryEvaluate(out string? expected))
            {
                throw new InvalidDataException($"The match expected expression is not resolved");
            }

            return actual.Equals(expected);
        }
    }
}
