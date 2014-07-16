// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Dashboard.Data
{
    public class ResultSegment<TResult> : IResultSegment<TResult>
    {
        private readonly IEnumerable<TResult> _results;
        private readonly string _continuationToken;

        public ResultSegment(IEnumerable<TResult> results, string continuationToken)
        {
            _results = results;
            _continuationToken = continuationToken;
        }

        public IEnumerable<TResult> Results
        {
            get { return _results; }
        }

        public string ContinuationToken
        {
            get { return _continuationToken; }
        }
    }
}
