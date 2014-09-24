// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Bindings.Data
{
    public class DataBindingFunctionalTests
    {
        private class MessageWithNonStringableProperty
        {
            public double DoubleValue { get; set; }
        }

        private static void TryToBindNonStringableParameter(
            [QueueTrigger("ignore")] MessageWithNonStringableProperty message,
            string doubleValue)
        {
        }

        [Fact]
        public void BindNonStringableParameter_FailsIndexing()
        {
            // Arrange
            MethodInfo method = typeof(DataBindingFunctionalTests).GetMethod("TryToBindNonStringableParameter",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method); // Guard

            FunctionIndexerContext context = FunctionIndexerContext.CreateDefault(null,
                new StorageAccount(CloudStorageAccount.DevelopmentStorageAccount), null, null);
            FunctionIndexer indexer = new FunctionIndexer(context);
            IFunctionIndex stubIndex = new Mock<IFunctionIndex>().Object;

            // Act & Assert
            Exception exception = Assert.Throws<InvalidOperationException>(
                () => indexer.IndexMethodAsyncCore(method, stubIndex, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal("Can't bind parameter 'doubleValue' to type 'System.String'.",
                exception.Message);
        }

        [Fact]
        public void BindStringableParameter_CanInvoke()
        {
            // Arrange
            using (var host = JobHostFactory.Create<StringableFunctions>())
            {
                MethodInfo method = typeof(StringableFunctions).GetMethod("BindStringableParameter");
                Assert.NotNull(method); // Guard
                Guid guid = Guid.NewGuid();
                string expectedGuidValue = guid.ToString("D");
                string message = JsonConvert.SerializeObject(new MessageWithStringableProperty { GuidValue = guid });

                try
                {
                    // Act
                    host.Call(method, new { message = message });

                    // Assert
                    Assert.Equal(expectedGuidValue, StringableFunctions.GuidValue);
                }
                finally
                {
                    StringableFunctions.GuidValue = null;
                }
            }
        }

        private class MessageWithStringableProperty
        {
            public Guid GuidValue { get; set; }
        }

        private class StringableFunctions
        {
            public static string GuidValue { get; set; }

            public static void BindStringableParameter([QueueTrigger("ignore")] MessageWithStringableProperty message,
                string guidValue)
            {
                GuidValue = guidValue;
            }
        }
    }
}
