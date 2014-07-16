// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.Jobs.Storage.Table
#else
namespace Microsoft.Azure.Jobs.Host.Storage.Table
#endif
{
    /// <summary>Represents a query modifier that filters by partition key.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class PartitionKeyEqualsQueryModifier : IQueryModifier
#else
    internal class PartitionKeyEqualsQueryModifier : IQueryModifier
#endif
    {
        private readonly string _partitionKey;

        /// <summary>Initializes a new instance of the <see cref="PartitionKeyEqualsQueryModifier"/> class.</summary>
        /// <param name="partitionKey">The partition key by which to filter.</param>
        public PartitionKeyEqualsQueryModifier(string partitionKey)
        {
            _partitionKey = partitionKey;
        }

        /// <inheritdoc />
        public IQueryable<T> Apply<T>(IQueryable<T> q) where T : ITableEntity
        {
            return q.Where(e => e.PartitionKey == _partitionKey);
        }
    }
}
