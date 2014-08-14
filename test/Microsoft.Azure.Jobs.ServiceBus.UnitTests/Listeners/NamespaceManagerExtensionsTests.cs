// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.TestCommon;
using Microsoft.Azure.Jobs.ServiceBus.Listeners;
using Microsoft.ServiceBus.Messaging;
using Xunit;

namespace Microsoft.Azure.Jobs.ServiceBus.UnitTests.Listeners
{
    public class NamespaceManagerExtensionsTests
    {
        private const string TestEntityPath = "long/fake/path";

        [Fact]
        public void SplitQueuePath_IfNonDLQPath_ReturnsOriginalPath()
        {
            string[] result = NamespaceManagerExtensions.SplitQueuePath(TestEntityPath);

            Assert.NotNull(result);
            Assert.Equal(1, result.Length);
            Assert.Equal(TestEntityPath, result[0]);
        }

        [Fact]
        public void SplitQueuePath_IfDLQPath_ReturnsPathToParentEntity()
        {
            string path = QueueClient.FormatDeadLetterPath(TestEntityPath);

            string[] result = NamespaceManagerExtensions.SplitQueuePath(path);

            Assert.NotNull(result);
            Assert.Equal(2, result.Length);
            Assert.Equal(TestEntityPath, result[0]);
        }

        [Fact]
        public void SplitQueuePath_IfNullArgument_Throws()
        {
            string path = null;

            ExceptionAssert.ThrowsArgument(() => NamespaceManagerExtensions.SplitQueuePath(path),
                "path", "path cannot be null or empty");
        }

        [Fact]
        public void SplitQueuePath_IfPathIsEmpty_Throws()
        {
            string path = "";

            ExceptionAssert.ThrowsArgument(() => NamespaceManagerExtensions.SplitQueuePath(path),
                "path", "path cannot be null or empty");
        }
    }
}
