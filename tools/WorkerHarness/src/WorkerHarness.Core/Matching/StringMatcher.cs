// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core.Matching
{
    public class StringMatcher : IMatcher
    {
        internal static string MatchingException = "Matching exception occurs: {0}";

        public bool Match(MatchingContext match, object source)
        {
            try
            {
                string queryResult = source.Query(match.Query);

                match.TryEvaluate(out string? expected);

                return string.Equals(queryResult, expected, StringComparison.Ordinal);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException(string.Format(MatchingException, ex.Message));
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
            catch (ArgumentException ex)
            {
                throw ex;
            }
        }
    }
}
