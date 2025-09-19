// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using AwesomeAssertions;
using Microsoft.Azure.WebJobs.Script.Pools;
using Microsoft.Extensions.ObjectPool;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Pools
{
    public class PoolRentalTests
    {
        [Fact]
        public void Gets_On_Creation_And_Returns_On_Dispose()
        {
            // arrange
            object value = new();
            Mock<ObjectPool<object>> pool = new(MockBehavior.Strict);
            pool.Setup(m => m.Get()).Returns(value);
            pool.Setup(m => m.Return(value));

            // act pt.1
            PoolRental<object> rental = new(pool.Object);

            // assert pt.1
            rental.Value.Should().BeSameAs(value);
            pool.Verify(m => m.Get(), Times.Once);
            pool.Verify(m => m.Return(It.IsAny<object>()), Times.Never);

            // act pt.2
            rental.Dispose();

            // assert pt.2
            pool.Verify(m => m.Get(), Times.Once);
            pool.Verify(m => m.Return(It.IsAny<object>()), Times.Once);
        }
    }
}
