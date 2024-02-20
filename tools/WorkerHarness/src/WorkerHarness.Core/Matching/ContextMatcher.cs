// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core.Matching
{
    public sealed class ContextMatcher : IContextMatcher
    {
        internal static string MatchingExceptionMessage = "An error occurs in the message matching";

        public bool Match(MatchingContext match, object source)
        {
            try
            {
                object rawQueryResult = source.Query(match.Query);

                string queryResult;
                if (rawQueryResult is string)
                {
                    queryResult = rawQueryResult.ToString() ?? string.Empty;
                }
                else
                {
                    queryResult = JsonSerializer.Serialize(rawQueryResult);
                }

                match.TryEvaluate(out string? expected);

                return string.Equals(queryResult, expected, StringComparison.Ordinal);
            }
            catch (ArgumentException ex)
            {
                string exMsg = $"{MatchingExceptionMessage}: {match.Query} == {match.Expected}. {ex.Message}";
                ArgumentException newEx = new(exMsg, ex);

                throw newEx;
            }
        }

        public bool MatchAll(IEnumerable<MatchingContext> matches, object source)
        {
            try
            {
                foreach (var match in matches)
                {
                    if (!Match(match, source))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
