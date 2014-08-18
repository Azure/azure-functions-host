// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dashboard.Data;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Moq;
using Xunit;

namespace Dashboard.UnitTests.Data
{
    public class VersionedMetadataTextStoreTests
    {
        [Fact]
        public void Constructor_Throws_IfInnerStoreIsNull()
        {
            // Arrange
            IConcurrentMetadataTextStore innerStore = null;
            IVersionMetadataMapper versionMapper = CreateDummyMapper();

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => CreateProductUnderTest(innerStore, versionMapper), "innerStore");
        }

        [Fact]
        public void Constructor_Throws_IfVersionMapperIsNull()
        {
            // Arrange
            IConcurrentMetadataTextStore innerStore = CreateDummyInnerStore();
            IVersionMetadataMapper versionMapper = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => CreateProductUnderTest(innerStore, versionMapper),
                "versionMapper");
        }

        [Fact]
        public void List_ReturnsNull_IfInnerStoreReturnsNull()
        {
            // Arrange
            string expectedPrefix = "Prefix";
            Mock<IConcurrentMetadataTextStore> innerStoreMock =
                new Mock<IConcurrentMetadataTextStore>(MockBehavior.Strict);
            innerStoreMock.Setup(s => s.List(expectedPrefix)).Returns((IEnumerable<ConcurrentMetadata>)null);
            IConcurrentMetadataTextStore innerStore = innerStoreMock.Object;
            IVersionMetadataMapper versionMapper = CreateDummyMapper();

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            // Act
            IEnumerable<VersionedMetadata> results = product.List(expectedPrefix);

            // Assert
            Assert.Null(results);
        }

        [Fact]
        public void List_ReturnsItem_IfInnerStoreReturnsItem()
        {
            // Arrange
            string expectedPrefix = "Prefix";
            string expectedId = "Id";
            string expectedETag = "ETag";
            IDictionary<string, string> expectedMetadata = new Dictionary<string, string>();
            DateTimeOffset expectedVersion = DateTimeOffset.Now;

            Mock<IConcurrentMetadataTextStore> innerStoreMock =
                new Mock<IConcurrentMetadataTextStore>(MockBehavior.Strict);
            innerStoreMock.Setup(s => s.List(expectedPrefix)).Returns(new ConcurrentMetadata[] {
                new ConcurrentMetadata(expectedId, expectedETag, expectedMetadata) });
            IConcurrentMetadataTextStore innerStore = innerStoreMock.Object;
            IVersionMetadataMapper versionMapper = CreateMapper(expectedMetadata, expectedVersion);

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            // Act
            IEnumerable<VersionedMetadata> results = product.List(expectedPrefix);

            // Assert
            VersionedMetadata expectedItem = new VersionedMetadata(expectedId, expectedETag, expectedMetadata,
                expectedVersion);
            Assert.NotNull(results);
            Assert.Equal(1, results.Count());
            VersionedMetadata result = results.First();
            AssertEqual(expectedItem, result);
        }

        [Fact]
        public void Read_ReturnsNull_IfInnerStoreReturnsNull()
        {
            // Arrange
            string expectedId = "Id";
            Mock<IConcurrentMetadataTextStore> innerStoreMock =
                new Mock<IConcurrentMetadataTextStore>(MockBehavior.Strict);
            innerStoreMock.Setup(s => s.Read(expectedId)).Returns((ConcurrentMetadataText)null);
            IConcurrentMetadataTextStore innerStore = innerStoreMock.Object;
            IVersionMetadataMapper versionMapper = CreateDummyMapper();

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            // Act
            VersionedMetadataText item = product.Read(expectedId);

            // Assert
            Assert.Null(item);
        }

        [Fact]
        public void Read_ReturnsItem_IfInnerStoreReturnsItem()
        {
            // Arrange
            string expectedId = "Id";
            string expectedETag = "ETag";
            IDictionary<string, string> expectedMetadata = new Dictionary<string, string>();
            DateTimeOffset expectedVersion = DateTimeOffset.Now;
            string expectedText = "Text";

            Mock<IConcurrentMetadataTextStore> innerStoreMock =
                new Mock<IConcurrentMetadataTextStore>(MockBehavior.Strict);
            innerStoreMock.Setup(s => s.Read(expectedId)).Returns(new ConcurrentMetadataText(expectedETag,
                expectedMetadata, expectedText));
            IConcurrentMetadataTextStore innerStore = innerStoreMock.Object;
            IVersionMetadataMapper versionMapper = CreateMapper(expectedMetadata, expectedVersion);

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            // Act
            VersionedMetadataText item = product.Read(expectedId);

            // Assert
            VersionedMetadataText expectedItem = new VersionedMetadataText(expectedETag, expectedMetadata,
                expectedVersion, expectedText);
            AssertEqual(expectedItem, item);
        }

        [Fact]
        public void CreateOrUpdateIfLatest_Creates_IfNotYetCreated()
        {
            // Arrange
            IConcurrentMetadataTextStore innerStore = CreateInnerStore();
            IVersionMetadataMapper versionMapper = CreateMapper();

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            string id = "Id";
            DateTimeOffset newVersion = DateTimeOffset.Now;
            IDictionary<string, string> newOtherMetadata = CreateMetadata("NewKey", "NewValue");
            string newText = "Text";

            // Act
            bool isLatest = product.CreateOrUpdateIfLatest(id, newVersion, newOtherMetadata, newText);

            // Assert
            Assert.True(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            Assert.NotNull(storedItem);
            IDictionary<string, string> expectedMetadata = CreateMetadata(newVersion, versionMapper, newOtherMetadata);
            ConcurrentMetadataText expectedItem = new ConcurrentMetadataText("1", expectedMetadata, newText);
            AssertEqual(expectedItem, storedItem);
        }

        [Fact]
        public void CreateOrUpdateIfLatest_Creates_IfNotYetCreatedAndOtherMetadataIsNull()
        {
            // Arrange
            IConcurrentMetadataTextStore innerStore = CreateInnerStore();
            IVersionMetadataMapper versionMapper = CreateMapper();

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            string id = "Id";
            DateTimeOffset newVersion = DateTimeOffset.Now;
            IDictionary<string, string> newOtherMetadata = null;
            string newText = "Text";

            // Act
            bool isLatest = product.CreateOrUpdateIfLatest(id, newVersion, newOtherMetadata, newText);

            // Assert
            Assert.True(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            Assert.NotNull(storedItem);
            IDictionary<string, string> expectedMetadata = CreateMetadata(newVersion, versionMapper);
            ConcurrentMetadataText expectedItem = new ConcurrentMetadataText("1", expectedMetadata, newText);
            AssertEqual(expectedItem, storedItem);
        }

        [Fact]
        public void CreateOrUpdateIfLatest_DoesNotUpdate_IfNewerExists()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            ConcurrentMetadataText existingItem = new ConcurrentMetadataText("1",
                CreateMetadata(DateTimeOffset.MaxValue, versionMapper, "ExistingKey", "ExistingValue"), "ExistingText");
            IConcurrentMetadataTextStore innerStore = CreateInnerStore(id, existingItem);

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            DateTimeOffset newVersion = DateTimeOffset.Now;
            IDictionary<string, string> newOtherMetadata = CreateMetadata("NewKey", "NewValue");
            string newText = "NewText";

            // Act
            bool isLatest = product.CreateOrUpdateIfLatest(id, newVersion, newOtherMetadata, newText);

            // Assert
            Assert.False(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            Assert.Same(existingItem, storedItem);
        }

        [Fact]
        public void CreateOrUpdateIfLatest_Updates_IfOlderExists()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            IConcurrentMetadataTextStore innerStore = CreateInnerStore(id, new ConcurrentMetadataText("1",
                CreateMetadata(DateTimeOffset.Now, versionMapper, "ExistingKey", "ExistingValue"),
                "ExistingText"));

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            DateTimeOffset newVersion = DateTimeOffset.MaxValue;
            IDictionary<string, string> newOtherMetadata = CreateMetadata("NewKey", "NewValue");
            string newText = "NewText";

            // Act
            bool isLatest = product.CreateOrUpdateIfLatest(id, newVersion, newOtherMetadata, newText);

            // Assert
            Assert.True(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            IDictionary<string, string> expectedMetadata = CreateMetadata(newVersion, versionMapper, newOtherMetadata);
            ConcurrentMetadataText expectedItem = new ConcurrentMetadataText("2", expectedMetadata, newText);
            AssertEqual(expectedItem, storedItem);
        }

        [Fact]
        public void CreateOrUpdateIfLatest_Updates_IfOlderExistsAndOtherMetadataIsNull()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            IConcurrentMetadataTextStore innerStore = CreateInnerStore(id, new ConcurrentMetadataText("1",
                CreateMetadata(DateTimeOffset.Now, versionMapper, "ExistingKey", "ExistingValue"),
                "ExistingText"));

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            DateTimeOffset newVersion = DateTimeOffset.MaxValue;
            IDictionary<string, string> newOtherMetadata = null;
            string newText = "NewText";

            // Act
            bool isLatest = product.CreateOrUpdateIfLatest(id, newVersion, newOtherMetadata, newText);

            // Assert
            Assert.True(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            IDictionary<string, string> expectedMetadata = CreateMetadata(newVersion, versionMapper);
            ConcurrentMetadataText expectedItem = new ConcurrentMetadataText("2", expectedMetadata, newText);
            AssertEqual(expectedItem, storedItem);
        }

        [Fact]
        public void CreateOrUpdateIfLatest_DoesNotUpdate_IfSameVersionExists()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            DateTimeOffset existingVersion = DateTimeOffset.MaxValue;
            IDictionary<string, string> existingOtherMetadata = CreateMetadata("ExistingKey", "ExistingValue");
            IDictionary<string, string> existingCombinedMetadata = CreateMetadata(existingVersion, versionMapper,
                existingOtherMetadata);
            string existingText = "ExstingText";
            ConcurrentMetadataText existingItem = new ConcurrentMetadataText("1", existingCombinedMetadata,
                existingText);
            IConcurrentMetadataTextStore innerStore = CreateInnerStore(id, existingItem);

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            // Act
            bool isLatest = product.CreateOrUpdateIfLatest(id, existingVersion, existingOtherMetadata, existingText);

            // Assert
            Assert.True(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            Assert.Same(existingItem, storedItem);
        }

        [Fact]
        public void CreateOrUpdateIfLatest_Creates_IfConcurrentlyDeleted()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            ConcurrentInnerStore innerStore = new ConcurrentInnerStore(id, new ConcurrentMetadataText("1",
                CreateMetadata(DateTimeOffset.Now, versionMapper, "ExistingKey", "ExistingValue"),
                "ExistingText"));
            innerStore.OnReadingMetadata += (calls) =>
            {
                if (calls == 0)
                {
                    Assert.True(innerStore.TryDelete(id, "1"));
                }
            };

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            DateTimeOffset newVersion = DateTimeOffset.MaxValue;
            IDictionary<string, string> newOtherMetadata = CreateMetadata("NewKey", "NewValue");
            string newText = "NewText";

            // Act
            bool isLatest = product.CreateOrUpdateIfLatest(id, newVersion, newOtherMetadata, newText);

            // Assert
            Assert.True(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            IDictionary<string, string> expectedMetadata = CreateMetadata(newVersion, versionMapper, newOtherMetadata);
            ConcurrentMetadataText expectedItem = new ConcurrentMetadataText("1", expectedMetadata, newText);
            AssertEqual(expectedItem, storedItem);
        }

        [Fact]
        public void CreateOrUpdateIfLatest_DoesNotUpdate_IfNewerConcurrentlyExists()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            ConcurrentInnerStore innerStore = new ConcurrentInnerStore(id, new ConcurrentMetadataText("1",
                CreateMetadata(DateTimeOffset.MinValue, versionMapper, "ExistingKey", "ExistingValue"),
                "ExistingText"));

            IDictionary<string, string> updatedMetadata = CreateMetadata(DateTimeOffset.MaxValue, versionMapper,
                "UpdatedKey", "UpdatedValue");
            string updatedText = "UpdatedText";

            innerStore.OnReadMetadata += (calls) =>
            {
                if (calls == 0)
                {
                    Assert.True(innerStore.TryUpdate(id, "1", updatedMetadata, updatedText));
                }
            };

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            DateTimeOffset newVersion = DateTimeOffset.Now;
            IDictionary<string, string> newOtherMetadata = CreateMetadata("NewKey", "NewValue");
            string newText = "NewText";

            // Act
            bool isLatest = product.CreateOrUpdateIfLatest(id, newVersion, newOtherMetadata, newText);

            // Assert
            Assert.False(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            ConcurrentMetadataText expectedItem = new ConcurrentMetadataText("2", updatedMetadata, updatedText);
            AssertEqual(expectedItem, storedItem);
        }

        [Fact]
        public void CreateOrUpdateIfLatest_Updates_IfOlderConcurrentlyExists()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            ConcurrentInnerStore innerStore = new ConcurrentInnerStore(id, new ConcurrentMetadataText("1",
                CreateMetadata(DateTimeOffset.Now, versionMapper, "ExistingKey", "ExistingValue"),
                "ExistingText"));

            IDictionary<string, string> updatedMetadata = CreateMetadata(DateTimeOffset.Now, versionMapper,
                "UpdatedKey", "UpdatedValue");
            string updatedText = "UpdatedText";

            innerStore.OnReadMetadata += (calls) =>
            {
                if (calls == 0)
                {
                    Assert.True(innerStore.TryUpdate(id, "1", updatedMetadata, updatedText));
                }
            };

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            DateTimeOffset newVersion = DateTimeOffset.MaxValue;
            IDictionary<string, string> newOtherMetadata = CreateMetadata("NewKey", "NewValue");
            string newText = "NewText";

            // Act
            bool isLatest = product.CreateOrUpdateIfLatest(id, newVersion, newOtherMetadata, newText);

            // Assert
            Assert.True(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            IDictionary<string, string> expectedMetadata = CreateMetadata(newVersion, versionMapper, newOtherMetadata);
            ConcurrentMetadataText expectedItem = new ConcurrentMetadataText("3", expectedMetadata, newText);
            AssertEqual(expectedItem, storedItem);
        }

        [Fact]
        public void CreateOrUpdateIfLatest_Throws_IfUpdateStopsMakingProgress()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            ConcurrentInnerStore innerStore = new ConcurrentInnerStore(id, new ConcurrentMetadataText("1",
                CreateMetadata(DateTimeOffset.MinValue, versionMapper, "ExistingKey", "ExistingValue"),
                "ExistingText"));

            IDictionary<string, string> updatedMetadata = CreateMetadata(DateTimeOffset.Now, versionMapper,
                "UpdatedKey", "UpdatedValue");
            string updatedText = "UpdatedText";

            // Simulate an erroneous inner store that returns false from TryUpdate even though the ETag has not changed.
            // A correct inner store would throw rather that return false if retrying can't help.
            innerStore.OnReadingMetadata += (calls) =>
            {
                if (calls == 1)
                {
                    Assert.True(innerStore.TryDelete(id, "2"));
                    Assert.True(innerStore.TryCreate(id, updatedMetadata, updatedText));
                }
            };
            innerStore.OnReadMetadata += (calls) =>
            {
                if (calls == 0)
                {
                    Assert.True(innerStore.TryUpdate(id, "1", updatedMetadata, updatedText));
                }
            };

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            DateTimeOffset newVersion = DateTimeOffset.MaxValue;
            IDictionary<string, string> newOtherMetadata = CreateMetadata("NewKey", "NewValue");
            string newText = "NewText";

            // Act & Assert
            ExceptionAssert.ThrowsInvalidOperation(
                () => product.CreateOrUpdateIfLatest(id, newVersion, newOtherMetadata, newText),
                "The operation stopped making progress.");
        }

        [Fact]
        public void CreateOrUpdateIfLatest_Throws_IfCreateKeepsLooping()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            ConcurrentInnerStore innerStore = new ConcurrentInnerStore(id, new ConcurrentMetadataText("1",
                CreateMetadata(DateTimeOffset.MinValue, versionMapper, "ExistingKey", "ExistingValue"),
                "ExistingText"));

            IDictionary<string, string> updatedMetadata = CreateMetadata(DateTimeOffset.Now, versionMapper,
                "UpdatedKey", "UpdatedValue");
            string updatedText = "UpdatedText";

            // Simulate an erroneous inner store that returns false from TryCreate but null from Read even though the
            // item exists. A correct inner store would throw rather that return false if retrying can't help.
            innerStore.OnReadingMetadata += (_) =>
            {
                Assert.True(innerStore.TryDelete(id, "1"));
            };
            innerStore.OnReadMetadata += (_) =>
            {
                Assert.True(innerStore.TryCreate(id, updatedMetadata, updatedText));
            };

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            DateTimeOffset newVersion = DateTimeOffset.MaxValue;
            IDictionary<string, string> newOtherMetadata = CreateMetadata("NewKey", "NewValue");
            string newText = "NewText";

            // Act & Assert
            ExceptionAssert.ThrowsInvalidOperation(
                () => product.CreateOrUpdateIfLatest(id, newVersion, newOtherMetadata, newText),
                "The operation gave up due to repeated failed creation attempts.");
        }

        [Fact]
        public void CreateOrUpdateIfLatest_Throws_IfTargetVersionIsMinValue()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            IConcurrentMetadataTextStore innerStore = CreateDummyInnerStore();

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            DateTimeOffset newVersion = DateTimeOffset.MinValue;
            IDictionary<string, string> newOtherMetadata = CreateMetadata("NewKey", "NewValue");
            string newText = "NewText";

            // Act & Assert
            ExceptionAssert.ThrowsArgument(
                () => product.CreateOrUpdateIfLatest(id, newVersion, newOtherMetadata, newText),
                "targetVersion", "targetVersion must be greater than DateTimeOffset.MinValue.");
        }

        [Fact]
        public void UpdateOrCreateIfLatestWithCurrentData_Updates_IfOlderExists()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            DateTimeOffset existingVersion = DateTimeOffset.Now;
            ConcurrentMetadataText existingItem = new ConcurrentMetadataText("1",
                CreateMetadata(existingVersion, versionMapper, "ExistingKey", "ExistingValue"), "ExistingText");
            IConcurrentMetadataTextStore innerStore = CreateInnerStore(id, existingItem);

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            DateTimeOffset newVersion = DateTimeOffset.MaxValue;
            IDictionary<string, string> newOtherMetadata = CreateMetadata("NewKey", "NewValue");
            string newText = "NewText";

            // Act
            bool isLatest = product.UpdateOrCreateIfLatest(id, newVersion, newOtherMetadata, newText, existingItem.ETag,
                existingVersion);

            // Assert
            Assert.True(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            IDictionary<string, string> expectedMetadata = CreateMetadata(newVersion, versionMapper, newOtherMetadata);
            ConcurrentMetadataText expectedItem = new ConcurrentMetadataText("2", expectedMetadata, newText);
            AssertEqual(expectedItem, storedItem);
        }

        [Fact]
        public void UpdateOrCreateIfLatestWithCurrentData_DoesNotUpdate_IfNewerExists()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            DateTimeOffset existingVersion = DateTimeOffset.MaxValue;
            ConcurrentMetadataText existingItem = new ConcurrentMetadataText("1",
                CreateMetadata(existingVersion, versionMapper, "ExistingKey", "ExistingValue"), "ExistingText");
            IConcurrentMetadataTextStore innerStore = CreateInnerStore(id, existingItem);

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            DateTimeOffset newVersion = DateTimeOffset.Now;
            IDictionary<string, string> newOtherMetadata = CreateMetadata("NewKey", "NewValue");
            string newText = "NewText";

            // Act
            bool isLatest = product.UpdateOrCreateIfLatest(id, newVersion, newOtherMetadata, newText, existingItem.ETag,
                existingVersion);

            // Assert
            Assert.False(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            Assert.Same(existingItem, storedItem);
        }

        [Fact]
        public void UpdateOrCreateIfLatestWithCurrentData_Creates_IfDeleted()
        {
            // Arrange
            IConcurrentMetadataTextStore innerStore = CreateInnerStore();
            IVersionMetadataMapper versionMapper = CreateMapper();

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            string id = "Id";
            DateTimeOffset newVersion = DateTimeOffset.MaxValue;
            IDictionary<string, string> newOtherMetadata = CreateMetadata("NewKey", "NewValue");
            string newText = "Text";

            // Act
            bool isLatest = product.UpdateOrCreateIfLatest(id, newVersion, newOtherMetadata, newText, "Z",
                DateTimeOffset.Now);

            // Assert
            Assert.True(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            Assert.NotNull(storedItem);
            IDictionary<string, string> expectedMetadata = CreateMetadata(newVersion, versionMapper, newOtherMetadata);
            ConcurrentMetadataText expectedItem = new ConcurrentMetadataText("1", expectedMetadata, newText);
            AssertEqual(expectedItem, storedItem);
        }

        [Fact]
        public void UpdateOrCreateIfLatestWithoutCurrentData_Updates_IfOlderExists()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            IConcurrentMetadataTextStore innerStore = CreateInnerStore(id, new ConcurrentMetadataText("1",
                CreateMetadata(DateTimeOffset.Now, versionMapper, "ExistingKey", "ExistingValue"),
                "ExistingText"));

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            DateTimeOffset newVersion = DateTimeOffset.MaxValue;
            IDictionary<string, string> newOtherMetadata = CreateMetadata("NewKey", "NewValue");
            string newText = "NewText";

            // Act
            bool isLatest = product.UpdateOrCreateIfLatest(id, newVersion, newOtherMetadata, newText);

            // Assert
            Assert.True(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            IDictionary<string, string> expectedMetadata = CreateMetadata(newVersion, versionMapper, newOtherMetadata);
            ConcurrentMetadataText expectedItem = new ConcurrentMetadataText("2", expectedMetadata, newText);
            AssertEqual(expectedItem, storedItem);
        }

        [Fact]
        public void DeleteIfLatestWithCurrentData_Deletes_IfOlderExists()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            DateTimeOffset existingVersion = DateTimeOffset.Now;
            ConcurrentMetadataText existingItem = new ConcurrentMetadataText("1",
                CreateMetadata(existingVersion, versionMapper, "ExistingKey", "ExistingValue"), "ExistingText");
            IConcurrentMetadataTextStore innerStore = CreateInnerStore(id, existingItem);

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            // Act
            bool isLatest = product.DeleteIfLatest(id, DateTimeOffset.MaxValue, existingItem.ETag, existingVersion);

            // Assert
            Assert.True(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            Assert.Null(storedItem);
        }

        [Fact]
        public void DeleteIfLatestWithCurrentData_DoesNotDelete_IfNewerExists()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            DateTimeOffset existingVersion = DateTimeOffset.MaxValue;
            ConcurrentMetadataText existingItem = new ConcurrentMetadataText("1",
                CreateMetadata(existingVersion, versionMapper, "ExistingKey", "ExistingValue"), "ExistingText");
            IConcurrentMetadataTextStore innerStore = CreateInnerStore(id, existingItem);

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            // Act
            bool isLatest = product.DeleteIfLatest(id, DateTimeOffset.Now, existingItem.ETag, existingVersion);

            // Assert
            Assert.False(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            Assert.Same(existingItem, storedItem);
        }

        [Fact]
        public void DeleteIfLatestWithCurrentData_Deletes_IfOlderConcurrentlyExists()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            ConcurrentInnerStore innerStore = new ConcurrentInnerStore(id, new ConcurrentMetadataText("2",
                CreateMetadata(DateTimeOffset.MinValue, versionMapper, "ExistingKey", "ExistingValue"),
                "ExistingText"));

            DateTimeOffset updatedVersion = DateTimeOffset.Now;
            IDictionary<string, string> updatedMetadata = CreateMetadata(updatedVersion, versionMapper, "UpdatedKey",
                "UpdatedValue");
            string updatedText = "UpdatedText";

            innerStore.OnReadingMetadata += (calls) =>
            {
                if (calls == 0)
                {
                    Assert.True(innerStore.TryUpdate(id, "2", updatedMetadata, updatedText));
                }
            };

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            // Act
            bool isLatest = product.DeleteIfLatest(id, DateTimeOffset.MaxValue, "1", updatedVersion);

            // Assert
            Assert.True(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            Assert.Null(storedItem);
        }

        [Fact]
        public void DeleteIfLatestWithCurrentData_Returns_IfConcurrentlyDeleted()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            DateTimeOffset existingVersion = DateTimeOffset.Now;
            ConcurrentInnerStore innerStore = new ConcurrentInnerStore(id, new ConcurrentMetadataText("2",
                CreateMetadata(existingVersion, versionMapper, "ExistingKey", "ExistingValue"), "ExistingText"));

            innerStore.OnReadingMetadata += (calls) =>
            {
                if (calls == 0)
                {
                    Assert.True(innerStore.TryDelete(id, "2"));
                }
            };

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            // Act
            bool isLatest = product.DeleteIfLatest(id, DateTimeOffset.MaxValue, "1", existingVersion);

            // Assert
            Assert.True(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            Assert.Null(storedItem);
        }

        [Fact]
        public void DeleteIfLatestWithoutCurrentData_Deletes_IfOlderExists()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            IConcurrentMetadataTextStore innerStore = CreateInnerStore(id, new ConcurrentMetadataText("1",
                CreateMetadata(DateTimeOffset.Now, versionMapper, "ExistingKey", "ExistingValue"),
                "ExistingText"));

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            // Act
            bool isLatest = product.DeleteIfLatest(id, DateTimeOffset.MaxValue);

            // Assert
            Assert.True(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            Assert.Null(storedItem);
        }

        [Fact]
        public void DeleteIfLatestWithoutCurrentData_DoesNotDelete_IfNewerExists()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            ConcurrentMetadataText existingItem = new ConcurrentMetadataText("1",
                CreateMetadata(DateTimeOffset.MaxValue, versionMapper, "ExistingKey", "ExistingValue"), "ExistingText");
            IConcurrentMetadataTextStore innerStore = CreateInnerStore(id, existingItem);

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            // Act
            bool isLatest = product.DeleteIfLatest(id, DateTimeOffset.Now);

            // Assert
            Assert.False(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            Assert.Same(existingItem, storedItem);
        }

        [Fact]
        public void DeleteIfLatestWithoutCurrentData_Deletes_IfOlderConcurrentlyExists()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            ConcurrentInnerStore innerStore = new ConcurrentInnerStore(id, new ConcurrentMetadataText("1",
                CreateMetadata(DateTimeOffset.MinValue, versionMapper, "ExistingKey", "ExistingValue"),
                "ExistingText"));

            IDictionary<string, string> updatedMetadata = CreateMetadata(DateTimeOffset.Now, versionMapper,
                "UpdatedKey", "UpdatedValue");
            string updatedText = "UpdatedText";

            innerStore.OnReadingMetadata += (calls) =>
            {
                if (calls == 0)
                {
                    Assert.True(innerStore.TryUpdate(id, "1", updatedMetadata, updatedText));
                }
            };

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            // Act
            bool isLatest = product.DeleteIfLatest(id, DateTimeOffset.MaxValue);

            // Assert
            Assert.True(isLatest);
            ConcurrentMetadataText storedItem = innerStore.Read(id);
            Assert.Null(storedItem);
        }

        [Fact]
        public void DeleteIfLatestWithoutCurrentData_Throws_IfStopsMakingProgress()
        {
            // Arrange
            IVersionMetadataMapper versionMapper = CreateMapper();
            string id = "Id";

            ConcurrentInnerStore innerStore = new ConcurrentInnerStore(id, new ConcurrentMetadataText("1",
                CreateMetadata(DateTimeOffset.MinValue, versionMapper, "ExistingKey", "ExistingValue"),
                "ExistingText"));

            IDictionary<string, string> updatedMetadata = CreateMetadata(DateTimeOffset.Now, versionMapper,
                "UpdatedKey", "UpdatedValue");
            string updatedText = "UpdatedText";

            // Simulate an erroneous inner store that returns false from TryDelete even though the ETag has not changed.
            // A correct inner store would throw rather that return false if retrying can't help.
            innerStore.OnReadingMetadata += (calls) =>
            {
                if (calls == 1)
                {
                    Assert.True(innerStore.TryDelete(id, "2"));
                    Assert.True(innerStore.TryCreate(id, updatedMetadata, updatedText));
                }
            };
            innerStore.OnReadMetadata += (calls) =>
            {
                if (calls == 0)
                {
                    Assert.True(innerStore.TryUpdate(id, "1", updatedMetadata, updatedText));
                }
            };

            IVersionedMetadataTextStore product = CreateProductUnderTest(innerStore, versionMapper);

            // Act & Assert
            ExceptionAssert.ThrowsInvalidOperation(() => product.DeleteIfLatest(id, DateTimeOffset.MaxValue),
                "The operation stopped making progress.");
        }

        private static void AssertEqual(ConcurrentMetadataText expected, ConcurrentMetadataText actual)
        {
            if (expected == null)
            {
                Assert.Null(actual);
                return;
            }

            Assert.NotNull(actual);
            Assert.Equal(expected.ETag, actual.ETag);
            Assert.Equal(expected.Metadata, actual.Metadata);
            Assert.Equal(expected.Text, actual.Text);
        }

        private static void AssertEqual(VersionedMetadata expected, VersionedMetadata actual)
        {
            if (expected == null)
            {
                Assert.Null(actual);
                return;
            }

            Assert.NotNull(actual);
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.ETag, actual.ETag);
            Assert.Equal(expected.Metadata, actual.Metadata);
            Assert.Equal(expected.Version, actual.Version);
        }

        private static void AssertEqual(VersionedMetadataText expected, VersionedMetadataText actual)
        {
            if (expected == null)
            {
                Assert.Null(actual);
                return;
            }

            Assert.NotNull(actual);
            Assert.Equal(expected.ETag, actual.ETag);
            Assert.Equal(expected.Metadata, actual.Metadata);
            Assert.Equal(expected.Version, actual.Version);
            Assert.Equal(expected.Text, actual.Text);
        }

        private static IVersionMetadataMapper CreateDummyMapper()
        {
            return new Mock<IVersionMetadataMapper>(MockBehavior.Strict).Object;
        }

        private static IConcurrentMetadataTextStore CreateDummyInnerStore()
        {
            return new Mock<IConcurrentMetadataTextStore>(MockBehavior.Strict).Object;
        }

        private static IConcurrentMetadataTextStore CreateInnerStore()
        {
            return CreateInnerStore(new Dictionary<string, ConcurrentMetadataText>());
        }

        private static IConcurrentMetadataTextStore CreateInnerStore(string id, ConcurrentMetadataText item)
        {
            return CreateInnerStore(new Dictionary<string, ConcurrentMetadataText> { { id, item } });
        }

        private static IConcurrentMetadataTextStore CreateInnerStore(IDictionary<string, ConcurrentMetadataText> values)
        {
            return new SimpleInnerStore(values);
        }

        private static VersionedMetadataTextStore CreateProductUnderTest(IConcurrentMetadataTextStore innerStore,
            IVersionMetadataMapper metadataMapper)
        {
            return new VersionedMetadataTextStore(innerStore, metadataMapper);
        }

        private static IVersionMetadataMapper CreateMapper()
        {
            const string versionKey = "Version";
            Mock<IVersionMetadataMapper> mock = new Mock<IVersionMetadataMapper>(MockBehavior.Strict);
            mock
                .Setup(m => m.GetVersion(It.IsAny<IDictionary<string, string>>()))
                .Returns<IDictionary<string, string>>(m =>
                    DateTimeOffset.ParseExact(m[versionKey], "o", CultureInfo.CurrentCulture, DateTimeStyles.None));
            mock.Setup(m => m.SetVersion(It.IsAny<DateTimeOffset>(), It.IsAny<IDictionary<string, string>>()))
                .Callback<DateTimeOffset, IDictionary<string, string>>((v, m) => m[versionKey] = v.ToString("o"));
            return mock.Object;
        }

        private static IVersionMetadataMapper CreateMapper(IDictionary<string, string> expectedMetadata,
            DateTimeOffset version)
        {
            Mock<IVersionMetadataMapper> mock = new Mock<IVersionMetadataMapper>(MockBehavior.Strict);
            mock.Setup(m => m.GetVersion(expectedMetadata)).Returns(version);
            return mock.Object;
        }

        private static IDictionary<string, string> CreateMetadata(string key, string value)
        {
            return new Dictionary<string, string> { { key, value } };
        }

        private static IDictionary<string, string> CreateMetadata(DateTimeOffset version, IVersionMetadataMapper mapper)
        {
            Dictionary<string, string> metadata = new Dictionary<string, string>();
            mapper.SetVersion(version, metadata);
            return metadata;
        }

        private static IDictionary<string, string> CreateMetadata(DateTimeOffset version, IVersionMetadataMapper mapper,
            IDictionary<string, string> otherMetadata)
        {
            Dictionary<string, string> combinedMetadata = new Dictionary<string, string>(otherMetadata);
            mapper.SetVersion(version, combinedMetadata);
            return combinedMetadata;
        }

        private static IDictionary<string, string> CreateMetadata(DateTimeOffset version, IVersionMetadataMapper mapper,
            string otherKey, string otherValue)
        {
            Dictionary<string, string> metadata = new Dictionary<string, string>();
            mapper.SetVersion(version, metadata);
            metadata.Add(otherKey, otherValue);
            return metadata;
        }

        private class SimpleInnerStore : IConcurrentMetadataTextStore
        {
            private readonly IDictionary<string, ConcurrentMetadataText> _values;

            public SimpleInnerStore(IDictionary<string, ConcurrentMetadataText> values)
            {
                _values = values;
            }

            public ConcurrentMetadataText Read(string id)
            {
                if (!_values.ContainsKey(id))
                {
                    return null;
                }

                return _values[id];
            }

            public ConcurrentMetadata ReadMetadata(string id)
            {
                if (!_values.ContainsKey(id))
                {
                    return null;
                }

                return new ConcurrentMetadata(id, _values[id].ETag, _values[id].Metadata);
            }

            public bool TryCreate(string id, IDictionary<string, string> metadata, string text)
            {
                if (_values.ContainsKey(id))
                {
                    return false;
                }

                _values.Add(id, new ConcurrentMetadataText("1", metadata, text));
                return true;
            }

            public bool TryUpdate(string id, string eTag, IDictionary<string, string> metadata, string text)
            {
                if (!_values.ContainsKey(id))
                {
                    return false;
                }

                ConcurrentMetadataText value = _values[id];

                if (value.ETag != eTag)
                {
                    return false;
                }

                _values[id] = new ConcurrentMetadataText((int.Parse(eTag) + 1).ToString(), metadata, text);
                return true;
            }

            public bool TryDelete(string id, string eTag)
            {
                if (!_values.ContainsKey(id))
                {
                    return false;
                }
                ConcurrentMetadataText value = _values[id];

                if (value.ETag != eTag)
                {
                    return false;
                }

                _values.Remove(id);
                return true;
            }

            public IEnumerable<ConcurrentMetadata> List(string prefix)
            {
                throw new NotImplementedException();
            }

            IConcurrentText IConcurrentTextStore.Read(string id)
            {
                throw new NotImplementedException();
            }

            public void CreateOrUpdate(string id, string text)
            {
                throw new NotImplementedException();
            }

            public void CreateOrUpdate(string id, IDictionary<string, string> metadata, string text)
            {
                throw new NotImplementedException();
            }

            public void DeleteIfExists(string id)
            {
                throw new NotImplementedException();
            }

            public bool TryCreate(string id, string text)
            {
                throw new NotImplementedException();
            }

            public bool TryUpdate(string id, string eTag, string text)
            {
                throw new NotImplementedException();
            }
        }

        private class ConcurrentInnerStore : IConcurrentMetadataTextStore
        {
            private readonly IDictionary<string, ConcurrentMetadataText> _values;

            private int _readCalls;

            public event Action<int> OnReadingMetadata;
            public event Action<int> OnReadMetadata;

            public ConcurrentInnerStore(string id, ConcurrentMetadataText item)
                : this(new Dictionary<string, ConcurrentMetadataText> { { id, item } })
            {
            }

            public ConcurrentInnerStore(IDictionary<string, ConcurrentMetadataText> values)
            {
                _values = values;
            }

            public ConcurrentMetadataText Read(string id)
            {
                if (!_values.ContainsKey(id))
                {
                    return null;
                }

                return _values[id];
            }

            public ConcurrentMetadata ReadMetadata(string id)
            {
                try
                {
                    if (OnReadingMetadata != null)
                    {
                        OnReadingMetadata(_readCalls);
                    }

                    if (!_values.ContainsKey(id))
                    {
                        return null;
                    }

                    return new ConcurrentMetadata(id, _values[id].ETag, _values[id].Metadata);
                }
                finally
                {
                    if (OnReadMetadata != null)
                    {
                        OnReadMetadata(_readCalls);
                    }

                    _readCalls++;
                }
            }

            public bool TryCreate(string id, IDictionary<string, string> metadata, string text)
            {
                if (_values.ContainsKey(id))
                {
                    return false;
                }

                _values.Add(id, new ConcurrentMetadataText("1", metadata, text));
                return true;
            }

            public bool TryUpdate(string id, string eTag, IDictionary<string, string> metadata, string text)
            {
                if (!_values.ContainsKey(id))
                {
                    return false;
                }

                ConcurrentMetadataText value = _values[id];

                if (value.ETag != eTag)
                {
                    return false;
                }

                _values[id] = new ConcurrentMetadataText((int.Parse(eTag) + 1).ToString(), metadata, text);
                return true;
            }

            public bool TryDelete(string id, string eTag)
            {
                if (!_values.ContainsKey(id))
                {
                    return false;
                }
                ConcurrentMetadataText value = _values[id];

                if (value.ETag != eTag)
                {
                    return false;
                }

                _values.Remove(id);
                return true;
            }

            public IEnumerable<ConcurrentMetadata> List(string prefix)
            {
                throw new NotImplementedException();
            }

            IConcurrentText IConcurrentTextStore.Read(string id)
            {
                throw new NotImplementedException();
            }

            public void CreateOrUpdate(string id, string text)
            {
                throw new NotImplementedException();
            }

            public void CreateOrUpdate(string id, IDictionary<string, string> metadata, string text)
            {
                throw new NotImplementedException();
            }

            public void DeleteIfExists(string id)
            {
                throw new NotImplementedException();
            }

            public bool TryCreate(string id, string text)
            {
                throw new NotImplementedException();
            }

            public bool TryUpdate(string id, string eTag, string text)
            {
                throw new NotImplementedException();
            }
        }
    }
}
