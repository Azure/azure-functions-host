// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using System.Text.RegularExpressions;
using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core.Validators
{
    internal class RegexValidator : IValidator
    {
        internal static string ValidationExceptionMessage = "An error occurs in the validation";

        public bool Validate(ValidationContext context, object message)
        {
            try
            {
                string query = context.Query;
                object rawQueryResult = message.Query(query);

                string queryResult;
                if (rawQueryResult is string)
                {
                    queryResult = rawQueryResult.ToString() ?? string.Empty;
                }
                else
                {
                    queryResult = JsonSerializer.Serialize(rawQueryResult);
                }

                context.TryEvaluate(out string? pattern);

                return pattern != null && Regex.IsMatch(queryResult, pattern);

            }
            catch (ArgumentException ex)
            {
                string exMsg = $"{ValidationExceptionMessage}: {context.Query} == {context.Expected}. {ex.Message}";
                ArgumentException newEx = new(exMsg, ex);

                throw newEx;
            }
        }
    }
}
