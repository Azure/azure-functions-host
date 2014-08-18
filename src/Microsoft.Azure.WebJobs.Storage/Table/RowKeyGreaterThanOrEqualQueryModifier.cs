// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Table
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Table
#endif
{
    /// <summary>Represents a query modifier that filters by row keys greater than or equal to a value.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class RowKeyGreaterThanOrEqualQueryModifier : IQueryModifier
#else
    internal class RowKeyGreaterThanOrEqualQueryModifier : IQueryModifier
#endif
    {
        private readonly string _rowKeyInclusiveLowerBound;

        /// <summary>
        /// Initializes a new instance of the <see cref="RowKeyGreaterThanOrEqualQueryModifier"/> class.
        /// </summary>
        /// <param name="rowKeyInclusiveLowerBound">The inclusive lower bound for the row key.</param>
        public RowKeyGreaterThanOrEqualQueryModifier(string rowKeyInclusiveLowerBound)
        {
            _rowKeyInclusiveLowerBound = rowKeyInclusiveLowerBound;
        }

        /// <inheritdoc />
        public IQueryable<T> Apply<T>(IQueryable<T> q) where T : ITableEntity
        {
            return q.Where(e => e.RowKey.CompareTo(_rowKeyInclusiveLowerBound) >= 0);
        }
    }
}
