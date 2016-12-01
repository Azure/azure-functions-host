// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    internal static class IEnumerableExtensions
    {
        public static IEnumerable<T> NotDefaults<T>(this IEnumerable<T> collection)
        {
            return collection.Where(e => !EqualityComparer<T>.Default.Equals(e, default(T)));
        }
    }
}