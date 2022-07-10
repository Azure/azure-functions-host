// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.RegularExpressions;
using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core.Validators
{
    internal class RegexValidator : IValidator
    {
        internal static string ValidationExceptionMessage = $"{typeof(RegexValidator)} exception occurs: ";

        public bool Validate(ValidationContext context, object message)
        {
            try
            {
                string query = context.Query;
                string queryResult = message.Query(query);

                context.TryEvaluate(out string? pattern);

                return pattern != null && Regex.IsMatch(queryResult, pattern);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException(string.Concat(ValidationExceptionMessage, ex.Message));
            }
        }
    }
}
