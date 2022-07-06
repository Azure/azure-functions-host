// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core
{
    public class StringMatcher : IMatcher
    {
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
                throw ex;
            }
        }

        public bool MatchAll(IEnumerable<MatchingContext> matches, object source)
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
    }
}
