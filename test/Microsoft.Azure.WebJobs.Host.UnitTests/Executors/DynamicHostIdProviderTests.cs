// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Storage;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class DynamicHostIdProviderTests
    {
        [Fact]
        public void GetHostIdAsync_IfStorageAccountProviderThrowsInvalidOperationException_WrapsException()
        {
            // Arrange
            Mock<IStorageAccountProvider> storageAccountProviderMock = new Mock<IStorageAccountProvider>(
                MockBehavior.Strict);
            TaskCompletionSource<IStorageAccount> taskSource = new TaskCompletionSource<IStorageAccount>();
            InvalidOperationException innerException = new InvalidOperationException();
            taskSource.SetException(innerException);
            storageAccountProviderMock
                .Setup(p => p.GetAccountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            IStorageAccountProvider storageAccountProvider = storageAccountProviderMock.Object;
            IFunctionIndexProvider functionIndexProvider = CreateDummyFunctionIndexProvider();

            IHostIdProvider product = new DynamicHostIdProvider(storageAccountProvider, () => functionIndexProvider);
            CancellationToken cancellationToken = CancellationToken.None;

            // Act & Assert
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => product.GetHostIdAsync(cancellationToken).GetAwaiter().GetResult());
            Assert.Equal("A host ID is required. Either set JobHostConfiguration.HostId or provide a valid storage " +
                "connection string.", exception.Message);
            Assert.Same(innerException, exception.InnerException);
        }

        private static IFunctionIndexProvider CreateDummyFunctionIndexProvider()
        {
            return new Mock<IFunctionIndexProvider>(MockBehavior.Strict).Object;
        }
    }
}
