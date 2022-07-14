// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.Matching
{
    public interface IContextMatcher
    {
        /// <summary>
        /// Determine if the source object matches the criteria specified in match.
        /// </summary>
        /// <param name="match" cref="MatchingContext"></param>
        /// <param name="source" cref="object"></param>
        /// <returns></returns>
        bool Match(MatchingContext match, object source);

        /// <summary>
        /// Determin if the source object matches a list of criteria
        /// </summary>
        /// <param name="matches" cref="IEnumerable{MatchingContext}"></param>
        /// <param name="source" cref="object"></param>
        /// <returns></returns>
        bool MatchAll(IEnumerable<MatchingContext> matches, object source);
    }
}
