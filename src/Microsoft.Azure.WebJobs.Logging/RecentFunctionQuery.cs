// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Query parameters to get recent function information. 
    /// </summary>
    public class RecentFunctionQuery
    {
        /// <summary>
        /// Name of the function to query for. 
        /// </summary>
        /// <remarks>This is case-sensitive, and the character set here is restricted to valid Azure Table characters. </remarks>
        public FunctionId FunctionId { get; set; }

        /// <summary>
        /// Maximum results to return in a segment. Used for pagination. 
        /// </summary>
        public int MaximumResults { get; set; }

        /// <summary>
        /// Inclusive Start of the time window to query. Can be Date.MinValue.
        /// </summary>
        public DateTime Start { get; set; }

        /// <summary>
        /// Exclusive end of the time window to query. Can be Date.MaxValue.
        /// </summary>
        public DateTime End { get; set; }

        /// <summary>
        /// Set to only include failures in the query. 
        /// </summary>
        public bool OnlyFailures { get; set; }
    }
}