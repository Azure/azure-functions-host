// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core.Validators
{
    internal class StringValidator : IValidator
    {
        internal static string ValidationException = "Validation exception occurs: {0}";

        public bool Validate(ValidationContext context, object message)
        {
            try
            {
                string query = context.Query;
                string queryResult = message.Query(query);

                context.TryEvaluate(out string? expected);

                return string.Equals(queryResult, expected, StringComparison.Ordinal);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException(string.Format(ValidationException, ex.Message));
            }

        }
    }
}
