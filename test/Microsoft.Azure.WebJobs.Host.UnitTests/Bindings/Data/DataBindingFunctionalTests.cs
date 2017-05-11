// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Triggers;
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

            FunctionIndexer indexer = FunctionIndexerFactory.Create(CloudStorageAccount.DevelopmentStorageAccount);
            IFunctionIndexCollector stubIndex = new Mock<IFunctionIndexCollector>().Object;

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
            using (var host = JobHostFactory.Create<TestFunctions>())
            {
                MethodInfo method = typeof(TestFunctions).GetMethod("BindStringableParameter");
                Assert.NotNull(method); // Guard
                Guid guid = Guid.NewGuid();
                string expectedGuidValue = guid.ToString("D");
                string message = JsonConvert.SerializeObject(new MessageWithStringableProperty { GuidValue = guid });

                try
                {
                    // Act
                    host.Call(method, new { message = message });

                    // Assert
                    Assert.Equal(expectedGuidValue, TestFunctions.Result);
                }
                finally
                {
                    TestFunctions.Result = null;
                }
            }
        }

        [Fact]
        public void ParameterBindings_WithNullableParameters_CanInvoke()
        {
            // Arrange
            using (var host = JobHostFactory.Create<TestFunctions>())
            {
                MethodInfo method = typeof(TestFunctions).GetMethod("ParameterBindings");
                TestFunctions.Result = null;
                host.Call(method, new { a = 123, b = default(int?), c = "Testing" });
                Assert.Equal("123Testing", TestFunctions.Result);

                host.Call(method, new { a = 123, b = default(int?), c = (string)null });
                Assert.Equal("123", TestFunctions.Result);
            }
        }

        private class MessageWithStringableProperty
        {
            public Guid GuidValue { get; set; }
        }

        private class TestFunctions
        {
            public static string Result { get; set; }

            public static void BindStringableParameter([QueueTrigger("ignore")] MessageWithStringableProperty message,
                string guidValue)
            {
                Result = guidValue;
            }

            [NoAutomaticTrigger]
            public static void ParameterBindings(int a, int? b, string c)
            {
                Result = $"{a}{b}{c}";
            }
        }
    }
}
