// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text;
using Microsoft.Extensions.ObjectPool;

namespace Microsoft.Azure.WebJobs.Script.Pools;

/// <summary>
/// A factory of object pools.
/// </summary>
/// <remarks>
/// This class makes it easy to create efficient object pools used to improve performance by reducing
/// strain on the garbage collector.
///
/// Code taken from: https://github.com/dotnet/extensions/blob/d32357716a5261509bf7527101b21cb6f94a0f89/src/Shared/Pools/PoolFactory.cs.
/// </remarks>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
internal static class PoolFactory
{
    internal const int DefaultCapacity = 1024;
    private const int DefaultMaxStringBuilderCapacity = 64 * 1024;
    private const int InitialStringBuilderCapacity = 128;

    private static readonly IPooledObjectPolicy<StringBuilder> _defaultStringBuilderPolicy = new StringBuilderPooledObjectPolicy
    {
        InitialCapacity = InitialStringBuilderCapacity,
        MaximumRetainedCapacity = DefaultCapacity
    };

    /// <summary>
    /// Gets the shared pool of <see cref="StringBuilder"/> instances.
    /// </summary>
    public static ObjectPool<StringBuilder> SharedStringBuilderPool { get; } = CreateStringBuilderPool();

    /// <summary>
    /// Creates a pool of <see cref="StringBuilder"/> instances.
    /// </summary>
    /// <param name="maxCapacity">The maximum number of items to keep in the pool. This defaults to 1024. This value is a recommendation, the pool may keep more objects than this.</param>
    /// <param name="maxStringBuilderCapacity">The maximum capacity of the string builders to keep in the pool. This defaults to 64K.</param>
    /// <returns>The pool.</returns>
    public static ObjectPool<StringBuilder> CreateStringBuilderPool(int maxCapacity = DefaultCapacity, int maxStringBuilderCapacity = DefaultMaxStringBuilderCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxStringBuilderCapacity, 1);

        if (maxStringBuilderCapacity == DefaultMaxStringBuilderCapacity)
        {
            return MakePool(_defaultStringBuilderPolicy, maxCapacity);
        }

        return MakePool(
            new StringBuilderPooledObjectPolicy
            {
                InitialCapacity = InitialStringBuilderCapacity,
                MaximumRetainedCapacity = maxStringBuilderCapacity
            }, maxCapacity);
    }

    private static DefaultObjectPool<T> MakePool<T>(IPooledObjectPolicy<T> policy, int maxRetained)
        where T : class
        => new(policy, maxRetained);
}