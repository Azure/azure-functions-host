// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dashboard.Data;
using Dashboard.Indexers;
using Microsoft.Azure.WebJobs.Protocols;
using Moq;
using Xunit;

namespace Dashboard.UnitTests.Indexers
{
    public class HostIndexerTests
    {
        [Fact]
        public void ProcessHostStarted_UpdatesOrCreatesHostSnapshotIfLatest()
        {
            // Arrange
            const string hostId = "abc";
            string[] expectedFunctionIds = new string[] { "a", "b" };
            DateTimeOffset expectedHostVersion = DateTimeOffset.Now;
            IHostIndexManager hostIndexManager = CreateFakeHostIndexManager();
            IHostIndexer product = CreateProductUnderTest(hostIndexManager);
            HostStartedMessage message = new HostStartedMessage
            {
                SharedQueueName = hostId,
                EnqueuedOn = expectedHostVersion,
                Functions = new FunctionDescriptor[]
                {
                    new FunctionDescriptor { Id = expectedFunctionIds[0]},
                    new FunctionDescriptor { Id = expectedFunctionIds[1]}
                }
            };

            // Act
            product.ProcessHostStarted(message);

            // Assert
            HostSnapshot hostSnapshot = hostIndexManager.Read(hostId);
            Assert.NotNull(hostSnapshot);
            Assert.Equal(expectedHostVersion, hostSnapshot.HostVersion);
            Assert.Equal(expectedFunctionIds, hostSnapshot.FunctionIds);
        }

        [Fact]
        public void ProcessHostStarted_UpdatesOrCreatesFunctionIndexVersionIfLatest()
        {
            // Arrange
            DateTimeOffset expectedVersion = DateTimeOffset.Now;
            FakeFunctionIndexVersionManager functionIndexVersionManager = new FakeFunctionIndexVersionManager();
            IHostIndexer product = CreateProductUnderTest(functionIndexVersionManager);
            HostStartedMessage message = new HostStartedMessage
            {
                EnqueuedOn = expectedVersion
            };

            // Act
            product.ProcessHostStarted(message);

            // Assert
            Assert.Equal(expectedVersion, functionIndexVersionManager.Current);
        }

        [Fact]
        public void ProcessHostStarted_DeletesRemovedFunctionsIfLatest()
        {
            // Arrange
            const string hostId = "host";
            DateTimeOffset hostVersion = DateTimeOffset.Now;
            string[] expectedFunctionIds = new string[] { "a", "c" };
            IHostIndexManager hostIndexManager = CreateStubHostIndexManager(
                CreateHostSnapshot(hostVersion, expectedFunctionIds), persistSucceeds: true);
            IFunctionIndexManager functionIndexManager = CreateFakeFunctionIndexManager();
            DateTimeOffset earlierHostVersion = DateTimeOffset.MinValue;
            AddFunctionToIndex(functionIndexManager, hostId, expectedFunctionIds[0], earlierHostVersion);
            AddFunctionToIndex(functionIndexManager, hostId, "b", earlierHostVersion);
            AddFunctionToIndex(functionIndexManager, hostId, expectedFunctionIds[1], earlierHostVersion);
            IHostIndexer product = CreateProductUnderTest(hostIndexManager, functionIndexManager);
            HostStartedMessage message = new HostStartedMessage
            {
                SharedQueueName = hostId,
                EnqueuedOn = hostVersion,
                Functions = new FunctionDescriptor[]
                {
                    new FunctionDescriptor { Id = expectedFunctionIds[0] },
                    new FunctionDescriptor { Id = expectedFunctionIds[1] },
                }
            };

            // Act
            product.ProcessHostStarted(message);

            // Assert
            IEnumerable<VersionedMetadata> functions = functionIndexManager.List(hostId);
            Assert.NotNull(functions); // Guard
            IEnumerable<string> functionIds = functions.Select(f => f.Id).ToArray();
            Assert.Equal(expectedFunctionIds, functionIds);
        }

        [Fact]
        public void ProcessHostStarted_CreatesNewFunctionsIfLatest()
        {
            // Arrange
            const string hostId = "host";
            DateTimeOffset hostVersion = DateTimeOffset.Now;
            string[] expectedFunctionIds = new string[] { "a", "b", "c" };
            IHostIndexManager hostIndexManager = CreateStubHostIndexManager(
                CreateHostSnapshot(hostVersion, expectedFunctionIds), persistSucceeds: true);
            IFunctionIndexManager functionIndexManager = CreateFakeFunctionIndexManager();
            DateTimeOffset earlierHostVersion = DateTimeOffset.MinValue;
            AddFunctionToIndex(functionIndexManager, hostId, expectedFunctionIds[0], earlierHostVersion);
            AddFunctionToIndex(functionIndexManager, hostId, expectedFunctionIds[2], earlierHostVersion);
            IHostIndexer product = CreateProductUnderTest(hostIndexManager, functionIndexManager);
            HostStartedMessage message = new HostStartedMessage
            {
                SharedQueueName = hostId,
                EnqueuedOn = hostVersion,
                Functions = new FunctionDescriptor[]
                {
                    new FunctionDescriptor { Id = expectedFunctionIds[0] },
                    new FunctionDescriptor { Id = expectedFunctionIds[1] },
                    new FunctionDescriptor { Id = expectedFunctionIds[2] }
                }
            };

            // Act
            product.ProcessHostStarted(message);

            // Assert
            IEnumerable<VersionedMetadata> functions = functionIndexManager.List(hostId);
            Assert.NotNull(functions); // Guard
            IEnumerable<string> functionIds = functions.Select(f => f.Id).ToArray();
            Assert.Equal(expectedFunctionIds, functionIds);
        }

        [Fact]
        public void ProcessHostStarted_UpdatesExistingFunctionsIfLatest()
        {
            // Arrange
            const string hostId = "host";
            DateTimeOffset expectedVersion = DateTimeOffset.Now;
            string[] expectedFunctionIds = new string[] { "a", "b", "c" };
            IHostIndexManager hostIndexManager = CreateStubHostIndexManager(
                CreateHostSnapshot(expectedVersion, expectedFunctionIds), persistSucceeds: true);
            IFunctionIndexManager functionIndexManager = CreateFakeFunctionIndexManager();
            DateTimeOffset earlierHostVersion = DateTimeOffset.MinValue;
            AddFunctionToIndex(functionIndexManager, hostId, expectedFunctionIds[0], earlierHostVersion);
            AddFunctionToIndex(functionIndexManager, hostId, expectedFunctionIds[1], earlierHostVersion);
            AddFunctionToIndex(functionIndexManager, hostId, expectedFunctionIds[2], earlierHostVersion);
            IHostIndexer product = CreateProductUnderTest(hostIndexManager, functionIndexManager);
            HostStartedMessage message = new HostStartedMessage
            {
                SharedQueueName = hostId,
                EnqueuedOn = expectedVersion,
                Functions = new FunctionDescriptor[]
                {
                    new FunctionDescriptor { Id = expectedFunctionIds[0] },
                    new FunctionDescriptor { Id = expectedFunctionIds[1] },
                    new FunctionDescriptor { Id = expectedFunctionIds[2] }
                }
            };

            // Act
            product.ProcessHostStarted(message);

            // Assert
            IEnumerable<VersionedMetadata> functions = functionIndexManager.List(hostId);
            Assert.NotNull(functions); // Guard
            IEnumerable<string> functionIds = functions.Select(f => f.Id).ToArray();
            Assert.Equal(expectedFunctionIds, functionIds);
            IEnumerable<DateTimeOffset> versions = functions.Select(f => f.Version).ToArray();
            Assert.Equal(new DateTimeOffset[] { expectedVersion, expectedVersion, expectedVersion }, versions);
        }

        [Fact]
        public void ProcessHostStarted_IfHostConcurrentlyRemoved_DeletesKnownFunctionsIfLatest()
        {
            // Arrange
            const string hostId = "host";
            string[] originalFunctionIds = new string[] { "a", "b" };
            IHostIndexManager concurrentlyRemoveFunctionsHostIndexManager = CreateStubHostIndexManager(
                existingSnapshot: null, persistSucceeds: true);
            IFunctionIndexManager functionIndexManager = CreateFakeFunctionIndexManager();
            DateTimeOffset earlierHostVersion = DateTimeOffset.MinValue;
            AddFunctionToIndex(functionIndexManager, hostId, originalFunctionIds[0], earlierHostVersion);
            AddFunctionToIndex(functionIndexManager, hostId, originalFunctionIds[1], earlierHostVersion);
            IHostIndexer product = CreateProductUnderTest(concurrentlyRemoveFunctionsHostIndexManager,
                functionIndexManager);
            HostStartedMessage message = new HostStartedMessage
            {
                SharedQueueName = hostId,
                EnqueuedOn = DateTimeOffset.Now,
                Functions = new FunctionDescriptor[]
                {
                    new FunctionDescriptor { Id = originalFunctionIds[0] },
                    new FunctionDescriptor { Id = originalFunctionIds[1] },
                    new FunctionDescriptor { Id = "c" }
                }
            };

            // Act
            product.ProcessHostStarted(message);

            // Assert
            IEnumerable<VersionedMetadata> functions = functionIndexManager.List(hostId);
            Assert.NotNull(functions); // Guard
            IEnumerable<string> functionIds = functions.Select(f => f.Id).ToArray();
            Assert.Equal(new string[0], functionIds);
        }

        [Fact]
        public void ProcessHostStarted_IfHostConcurrentlyUpdated_DeletesKnownFunctionsIfLatest()
        {
            // Arrange
            const string hostId = "host";
            string[] originalFunctionIds = new string[] { "a", "b" };
            string[] finalFunctionIds = new string[] { "b", "d" };
            IHostIndexManager concurrentlyRemoveFunctionsHostIndexManager = CreateStubHostIndexManager(
                existingSnapshot: CreateHostSnapshot(DateTimeOffset.MaxValue, finalFunctionIds), persistSucceeds: true);
            IFunctionIndexManager functionIndexManager = CreateFakeFunctionIndexManager();
            DateTimeOffset earlierHostVersion = DateTimeOffset.MinValue;
            AddFunctionToIndex(functionIndexManager, hostId, originalFunctionIds[0], earlierHostVersion);
            AddFunctionToIndex(functionIndexManager, hostId, originalFunctionIds[1], earlierHostVersion);
            IHostIndexer product = CreateProductUnderTest(concurrentlyRemoveFunctionsHostIndexManager,
                functionIndexManager);
            HostStartedMessage message = new HostStartedMessage
            {
                SharedQueueName = hostId,
                EnqueuedOn = DateTimeOffset.Now,
                Functions = new FunctionDescriptor[]
                {
                    new FunctionDescriptor { Id = originalFunctionIds[0] },
                    new FunctionDescriptor { Id = originalFunctionIds[1] },
                    new FunctionDescriptor { Id = "c" },
                    new FunctionDescriptor { Id = "d" }
                }
            };

            // Act
            product.ProcessHostStarted(message);

            // Assert
            IEnumerable<VersionedMetadata> functions = functionIndexManager.List(hostId);
            Assert.NotNull(functions); // Guard
            IEnumerable<string> functionIds = functions.Select(f => f.Id).ToArray();
            Assert.Equal(finalFunctionIds, functionIds);
        }

        [Fact]
        public void ProcessHostStarted_IfHostPreviouslyRemovedButProcessingAborted_DeletesKnownFunctionsIfLatest()
        {
            // Arrange
            const string hostId = "host";
            DateTimeOffset hostVersion = DateTimeOffset.Now;
            string[] previouslyProcessedFunctionIds = new string[] { "a", "b", "c" };
            IHostIndexManager concurrentlyRemoveFunctionsHostIndexManager = CreateStubHostIndexManager(
                existingSnapshot: null, persistSucceeds: false);
            IFunctionIndexManager functionIndexManager = CreateFakeFunctionIndexManager();
            AddFunctionToIndex(functionIndexManager, hostId, previouslyProcessedFunctionIds[0], hostVersion);
            AddFunctionToIndex(functionIndexManager, hostId, previouslyProcessedFunctionIds[1], hostVersion);
            AddFunctionToIndex(functionIndexManager, hostId, previouslyProcessedFunctionIds[2], hostVersion);
            IHostIndexer product = CreateProductUnderTest(concurrentlyRemoveFunctionsHostIndexManager,
                functionIndexManager);
            HostStartedMessage message = new HostStartedMessage
            {
                SharedQueueName = hostId,
                EnqueuedOn = hostVersion,
                Functions = new FunctionDescriptor[]
                {
                    new FunctionDescriptor { Id = previouslyProcessedFunctionIds[0] },
                    new FunctionDescriptor { Id = previouslyProcessedFunctionIds[1] },
                    new FunctionDescriptor { Id = previouslyProcessedFunctionIds[2] }
                }
            };

            // Act
            product.ProcessHostStarted(message);

            // Assert
            IEnumerable<VersionedMetadata> functions = functionIndexManager.List(hostId);
            Assert.NotNull(functions); // Guard
            IEnumerable<string> functionIds = functions.Select(f => f.Id).ToArray();
            Assert.Equal(new string[0], functionIds);
        }

        private static void AddFunctionToIndex(IFunctionIndexManager functionIndexManager, string hostId,
            string hostFunctionId, DateTimeOffset hostVersion)
        {
            Assert.True(functionIndexManager.CreateOrUpdateIfLatest(new FunctionSnapshot
            {
                Id = hostId + "_" + hostFunctionId,
                HostFunctionId = hostFunctionId,
                QueueName = hostId,
                HostVersion = hostVersion
            }));
        }

        private static IFunctionIndexManager CreateFakeFunctionIndexManager()
        {
            return new FakeFunctionIndexManager();
        }

        private static IHostIndexManager CreateFakeHostIndexManager()
        {
            return new FakeHostIndexManager();
        }

        private static HostSnapshot CreateHostSnapshot(DateTimeOffset hostVersion, IEnumerable<string> functionIds)
        {
            return new HostSnapshot
            {
                HostVersion = hostVersion,
                FunctionIds = functionIds
            };
        }

        private static HostIndexer CreateProductUnderTest(IFunctionIndexVersionManager functionIndexVersionManager)
        {
            IHostIndexManager hostIndexManager = CreateStubHostIndexManager(existingSnapshot: null,
                persistSucceeds: false);
            IFunctionIndexManager functionIndexManager = CreateStubFunctionIndexManager();
            return CreateProductUnderTest(hostIndexManager, functionIndexManager, functionIndexVersionManager);
        }

        private static HostIndexer CreateProductUnderTest(IHostIndexManager hostIndexManager)
        {
            IFunctionIndexManager functionIndexManager = CreateStubFunctionIndexManager();
            IFunctionIndexVersionManager functionIndexVersionManager = CreateStubFunctionIndexVersionManager();
            return CreateProductUnderTest(hostIndexManager, functionIndexManager, functionIndexVersionManager);
        }

        private static HostIndexer CreateProductUnderTest(IHostIndexManager hostIndexManager,
            IFunctionIndexManager functionIndexManager)
        {
            IFunctionIndexVersionManager functionIndexVersionManager = CreateStubFunctionIndexVersionManager();
            return CreateProductUnderTest(hostIndexManager, functionIndexManager, functionIndexVersionManager);
        }

        private static HostIndexer CreateProductUnderTest(IHostIndexManager hostIndexManager,
            IFunctionIndexManager functionIndexManager, IFunctionIndexVersionManager functionIndexVersionManager)
        {
            return new HostIndexer(hostIndexManager, functionIndexManager, functionIndexVersionManager);
        }

        private static IFunctionIndexManager CreateStubFunctionIndexManager()
        {
            Mock<IFunctionIndexManager> mock = new Mock<IFunctionIndexManager>(MockBehavior.Strict);
            mock.Setup(m => m.List(It.IsAny<string>()))
                .Returns(Enumerable.Empty<VersionedMetadata>());
            mock.Setup(m => m.CreateOrUpdateIfLatest(It.IsAny<FunctionSnapshot>()))
                .Returns(false);
            mock.Setup(m => m.UpdateOrCreateIfLatest(It.IsAny<FunctionSnapshot>(), It.IsAny<string>(),
                    It.IsAny<DateTimeOffset>()))
                .Returns(false);
            mock.Setup(m => m.DeleteIfLatest(It.IsAny<string>(), It.IsAny<DateTimeOffset>()))
                .Returns(false);
            mock.Setup(m => m.DeleteIfLatest(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>(),
                    It.IsAny<DateTimeOffset>()))
                .Returns(false);
            return mock.Object;
        }

        private static IFunctionIndexVersionManager CreateStubFunctionIndexVersionManager()
        {
            Mock<IFunctionIndexVersionManager> mock = new Mock<IFunctionIndexVersionManager>(MockBehavior.Strict);
            mock.Setup(m => m.UpdateOrCreateIfLatest(It.IsAny<DateTimeOffset>()));
            return mock.Object;
        }

        private static IHostIndexManager CreateStubHostIndexManager(HostSnapshot existingSnapshot, bool persistSucceeds)
        {
            Mock<IHostIndexManager> mock = new Mock<IHostIndexManager>(MockBehavior.Strict);
            mock.Setup(m => m.Read(It.IsAny<string>()))
                .Returns(existingSnapshot);
            mock.Setup(m => m.UpdateOrCreateIfLatest(It.IsAny<string>(), It.IsAny<HostSnapshot>()))
                .Returns(persistSucceeds);
            return mock.Object;
        }
    }
}
