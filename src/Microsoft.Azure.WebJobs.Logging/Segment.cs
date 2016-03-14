// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Segmented array class to return Segments of a large query.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Segment<T>
    {
        /// <summary>
        /// Empty constructor
        /// </summary>
        public Segment()
        {
        }

        /// <summary>
        /// Construct around a given array, with no continuation.
        /// </summary>
        /// <param name="results">the results in this segment.</param>
        public Segment(T[] results)
            : this(results, null)
        {
        }

        /// <summary>
        /// Construct with a continuation token. 
        /// </summary>
        /// <param name="results">the results in this segment.</param>
        /// <param name="continuationToken">continuation token to get the next segment.</param>
        public Segment(T[] results, string continuationToken)
        {
            this.Results = results;
            this.ContinuationToken = continuationToken;
        }

        /// <summary>
        /// Items in this segment.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public T[] Results { get; set; }

        /// <summary>
        /// Continuation token to get the next segment. 
        /// </summary>
        public string ContinuationToken { get; set; }
    }
}