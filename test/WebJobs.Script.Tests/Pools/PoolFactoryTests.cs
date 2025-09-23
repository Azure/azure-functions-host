// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text;
using AwesomeAssertions;
using Microsoft.Azure.WebJobs.Script.Pools;
using Microsoft.Extensions.ObjectPool;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Pools
{
    public class PoolFactoryTests
    {
        [Fact]
        public void SharedStringBuilderPool()
        {
            // arrange
            ObjectPool<StringBuilder> pool = PoolFactory.SharedStringBuilderPool;

            // act
            StringBuilder sb = pool.Get();

            // assert
            sb.Should().NotBeNull();
            pool.Return(sb);
        }

        [Fact]
        public void StringBuilderPool()
        {
            // arrange
            ObjectPool<StringBuilder> pool = PoolFactory.CreateStringBuilderPool(123, 2048);

            // act
            StringBuilder sb = pool.Get();
            sb.Append('x', 4096);
            pool.Return(sb);
            StringBuilder sb2 = pool.Get();

            // assert
            sb.Should().NotBeSameAs(sb2);
        }
    }
}
