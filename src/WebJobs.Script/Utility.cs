// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class Utility
    {
        public static IReadOnlyDictionary<string, string> ToStringValues(this IReadOnlyDictionary<string, object> data)
        {
            return data.ToDictionary(p => p.Key, p => p.Value != null ? p.Value.ToString() : null, StringComparer.OrdinalIgnoreCase);
        }

        public static string GetFunctionShortName(string functionName)
        {
            int idx = functionName.LastIndexOf('.');
            if (idx > 0)
            {
                functionName = functionName.Substring(idx + 1);
            }

            return functionName;
        }

        public static string FlattenException(Exception ex, Func<string, string> sourceFormatter = null)
        {
            StringBuilder flattenedErrorsBuilder = new StringBuilder();
            string lastError = null;
            sourceFormatter = sourceFormatter ?? ((s) => s);

            if (ex is AggregateException)
            {
                ex = ex.InnerException;
            }

            do
            {
                StringBuilder currentErrorBuilder = new StringBuilder();
                if (!string.IsNullOrEmpty(ex.Source))
                {
                    currentErrorBuilder.AppendFormat("{0}: ", sourceFormatter(ex.Source));
                }

                currentErrorBuilder.Append(ex.Message);

                if (!ex.Message.EndsWith("."))
                {
                    currentErrorBuilder.Append(".");
                }

                // sometimes inner exceptions are exactly the same
                // so first check before duplicating
                string currentError = currentErrorBuilder.ToString();
                if (lastError == null ||
                    string.Compare(lastError.Trim(), currentError.Trim()) != 0)
                {
                    if (flattenedErrorsBuilder.Length > 0)
                    {
                        flattenedErrorsBuilder.Append(" ");
                    }
                    flattenedErrorsBuilder.Append(currentError);
                }

                lastError = currentError;
            }
            while ((ex = ex.InnerException) != null);

            return flattenedErrorsBuilder.ToString();
        }

        /// <summary>
        /// Applies any additional binding data from the input value to the specified binding data.
        /// This binding data then becomes available to the binding process (in the case of late bound bindings)
        /// </summary>
        internal static void ApplyBindingData(object value, Dictionary<string, object> bindingData)
        {
            try
            {
                // if the input value is a JSON string, extract additional
                // binding data from it
                string json = value as string;
                if (!string.IsNullOrEmpty(json) && Utility.IsJson(json))
                {
                    // parse the object skipping any nested objects (binding data
                    // only includes top level properties)
                    JObject parsed = JObject.Parse(json);
                    var additionalBindingData = parsed.Children<JProperty>()
                        .Where(p => p.Value != null &&
                        (p.Value.Type != JTokenType.Object & p.Value.Type != JTokenType.Array))
                        .ToDictionary(p => p.Name, p => (string)p);

                    if (additionalBindingData != null)
                    {
                        foreach (var item in additionalBindingData)
                        {
                            if (item.Value != null)
                            {
                                bindingData[item.Key] = item.Value;
                            }
                        }
                    }
                }
            }
            catch
            {
                // it's not an error if the incoming message isn't JSON
                // there are cases where there will be output binding parameters
                // that don't bind to JSON properties
            }
        }

        public static bool IsJson(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            input = input.Trim();
            return (input.StartsWith("{", StringComparison.OrdinalIgnoreCase) && input.EndsWith("}", StringComparison.OrdinalIgnoreCase))
                || (input.StartsWith("[", StringComparison.OrdinalIgnoreCase) && input.EndsWith("]", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Converts the first letter of the specified string to lower case if it
        /// isn't already.
        /// </summary>
        public static string ToLowerFirstCharacter(string input)
        {
            if (!string.IsNullOrEmpty(input) && char.IsUpper(input[0]))
            {
                input = char.ToLowerInvariant(input[0]) + input.Substring(1);
            }

            return input;
        }
    }
}
