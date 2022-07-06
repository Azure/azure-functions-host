using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core.Validators
{
    internal class RegexValidator : IValidator
    {
        /// <summary>
        /// Validate a message given a validation context
        /// The validation context contains 2 fields:
        ///     - expected: the expected string value that is a regex expression
        ///     - query: the query that identify a string value inside message
        ///         E.g. message = {A: {B: "hello"}}. Then query "message.A.B" yields "hello"
        /// If the query is hello, the method will determine if the query match the regex expression
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public bool Validate(ValidationContext context, object message)
        {
            //// context has the query and and expected value
            //if (context.Query == null || context.Expected == null)
            //{
            //    throw new InvalidDataException($"Either {nameof(context.Query)} or {nameof(context.Expected)} is null. Both are required for validation");
            //}

            //// index into message to find the property to validate
            //// 1. extract properties from context.Query and put them to an array. E.g "root.A.B.C" => [A, B, C]
            //int firstDotPosition = context.Query.IndexOf(".");
            //string propertiesInString = context.Query[(firstDotPosition + 1)..];
            //string[] propertiesInArray = propertiesInString.Split(".");

            //// convert message to a JsonNode
            //JsonSerializerOptions options = new JsonSerializerOptions() { WriteIndented = true };
            //options.Converters.Add(new JsonStringEnumConverter());
            //string messageAsJsonString = JsonSerializer.Serialize(message, options);
            //JsonNode messageAsJsonNode = JsonNode.Parse(messageAsJsonString) ?? throw new InvalidOperationException($"Cannot convert a string to a JsonNode");

            //// call VariableHelper.RecursiveIndex to find the actual value
            //JsonNode actualValueAsObject = VariableHelper.RecursiveIndex(messageAsJsonNode, propertiesInArray, 0);
            //// try to extract a string value from JsonNode. If JsonNode is not of type JsonValue, the underlying value is not a string
            //if (actualValueAsObject is not JsonValue)
            //{
            //    throw new InvalidDataException($"The current query {context.Query} return a non-string object. The application only support string validation");
            //}
            //string actualValueAsString = actualValueAsObject.AsValue().ToString();

            //return Regex.IsMatch(actualValueAsString, context.Expected);

            try
            {
                string query = context.Query;
                string queryResult = message.Query(query);

                return Regex.IsMatch(queryResult, context.Expected);
            }
            catch (ArgumentException ex)
            {
                throw ex;
            }
        }
    }
}
