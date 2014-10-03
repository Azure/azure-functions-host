// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Table
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Table
#endif
{
    /// <summary>Defines an entity resolver.</summary>
#if PUBLICSTORAGE
    public interface IEntityResolver
#else
    internal interface IEntityResolver
#endif
    {
        /// <summary>Gets the type of entity returned by <see cref="Resolve"/>.</summary>
        Type EntityType { get; }

        /// <summary>Resolves an entity.</summary>
        /// <param name="partitionKey">The partition key of the entity.</param>
        /// <param name="rowKey">The row key of the entity.</param>
        /// <param name="timestamp">The timestamp of the entity.</param>
        /// <param name="properties">The properties of the entity.</param>
        /// <param name="eTag">The ETag of the entity.</param>
        /// <returns>The entity resolved.</returns>
        object Resolve(string partitionKey, string rowKey, DateTimeOffset timestamp,
            IDictionary<string, EntityProperty> properties, string eTag);
    }
}
