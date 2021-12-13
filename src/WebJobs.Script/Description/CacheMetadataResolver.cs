// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public sealed class CacheMetadataResolver : MetadataReferenceResolver
    {
        private readonly MetadataReferenceResolver _innerResolver;
        private static readonly ConcurrentDictionary<string, ImmutableArray<PortableExecutableReference>> _referenceCache = new ConcurrentDictionary<string, ImmutableArray<PortableExecutableReference>>();

        public CacheMetadataResolver(MetadataReferenceResolver innerResolver)
        {
            _innerResolver = innerResolver ?? throw new ArgumentNullException(nameof(innerResolver));
        }

        public override bool Equals(object other)
        {
            return _innerResolver.Equals(other);
        }

        public override int GetHashCode()
        {
            return _innerResolver.GetHashCode();
        }

        public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties)
        {
            string cacheKey = $"{reference}:{baseFilePath}";
            if (_referenceCache.TryGetValue(cacheKey, out ImmutableArray<PortableExecutableReference> result))
            {
                return result;
            }

            result = _innerResolver.ResolveReference(reference, baseFilePath, properties);

            if (result.Length > 0)
            {
                _referenceCache[cacheKey] = result;
            }

            return result;
        }
    }
}
