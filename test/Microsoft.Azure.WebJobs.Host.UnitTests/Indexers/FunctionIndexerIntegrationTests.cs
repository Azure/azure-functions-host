// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Indexers
{
    public class FunctionIndexerIntegrationTests
    {
        // Helper to do the indexing.
        private static Tuple<FunctionDescriptor, IFunctionDefinition> IndexMethod(string methodName, INameResolver nameResolver = null)
        {
            MethodInfo method = typeof(FunctionIndexerIntegrationTests).GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(method);

            FunctionIndexer indexer = FunctionIndexerFactory.Create(CloudStorageAccount.DevelopmentStorageAccount,
                nameResolver);

            Tuple<FunctionDescriptor, IFunctionDefinition> indexEntry = null;
            Mock<IFunctionIndexCollector> indexMock = new Mock<IFunctionIndexCollector>(MockBehavior.Strict);
            indexMock
                .Setup((i) => i.Add(
                    It.IsAny<IFunctionDefinition>(),
                    It.IsAny<FunctionDescriptor>(),
                    It.IsAny<MethodInfo>()))
                .Callback<IFunctionDefinition, FunctionDescriptor, MethodInfo>(
                    (ifd, fd, i) => indexEntry = Tuple.Create(fd, ifd));
            IFunctionIndexCollector index = indexMock.Object;

            indexer.IndexMethodAsync(method, index, CancellationToken.None).GetAwaiter().GetResult();

            return indexEntry;
        }

        private static void NoAutoTrigger1([Blob(@"daas-test-input/{name}.csv")] TextReader inputs) { }

        [Fact]
        public void TestNoAutoTrigger1()
        {
            var entry = IndexMethod("NoAutoTrigger1");

            Assert.NotNull(entry);

            IFunctionDefinition definiton = entry.Item2;
            Assert.NotNull(definiton);
            Assert.Null(definiton.ListenerFactory);

            FunctionDescriptor descriptor = entry.Item1;
            Assert.NotNull(descriptor);
            var parameters = descriptor.Parameters;
            Assert.Equal(1, parameters.Count());
            Assert.IsType<BlobParameterDescriptor>(parameters.First());
        }

        private static void NameResolver([Blob(@"input/%name%")] TextReader inputs) { }

        [Fact]
        public void TestNameResolver()
        {
            DictNameResolver nameResolver = new DictNameResolver();
            nameResolver.Add("name", "VALUE");

            FunctionDescriptor func = IndexMethod("NameResolver", nameResolver).Item1;

            Assert.NotNull(func);
            var parameters = func.Parameters;
            Assert.Equal(1, parameters.Count());
            ParameterDescriptor firstParameter = parameters.First();
            Assert.Equal("inputs", firstParameter.Name);
            Assert.IsType<BlobParameterDescriptor>(firstParameter);
            BlobParameterDescriptor blobParameter = (BlobParameterDescriptor)firstParameter;
            Assert.Equal(@"input", blobParameter.ContainerName);
            Assert.Equal(@"VALUE", blobParameter.BlobName);
        }

        public static void AutoTrigger1([BlobTrigger(@"daas-test-input/{name}.csv")] TextReader inputs) { }

        [Fact]
        public void TestAutoTrigger1()
        {
            FunctionDescriptor func = IndexMethod("AutoTrigger1").Item1;

            Assert.NotNull(func);
            var parameters = func.Parameters;
            Assert.Equal(1, parameters.Count());
            Assert.IsType<BlobTriggerParameterDescriptor>(parameters.First());
        }

        [NoAutomaticTrigger]
        public static void NoAutoTrigger2(int x, int y) { }

        [Fact]
        public void TestNoAutoTrigger2()
        {
            var entry = IndexMethod("NoAutoTrigger2");

            Assert.NotNull(entry);

            IFunctionDefinition definiton = entry.Item2;
            Assert.NotNull(definiton);
            Assert.Null(definiton.ListenerFactory);

            FunctionDescriptor descriptor = entry.Item1;
            Assert.NotNull(descriptor);
            var parameters = descriptor.Parameters;
            Assert.Equal(2, parameters.Count());
            Assert.IsType<CallerSuppliedParameterDescriptor>(parameters.ElementAt(0));
            Assert.IsType<CallerSuppliedParameterDescriptor>(parameters.ElementAt(1));
        }

        // Nothing about this method that is indexable.
        // No function (no trigger)
        public static void NoIndex(int x, int y) { }

        [Fact]
        public void TestNoIndex()
        {
            var entry = IndexMethod("NoIndex");

            Assert.Null(entry);
        }

        private static void Table([Table("TableName")] CloudTable table) { }

        [Fact]
        public void TestTable()
        {
            FunctionDescriptor func = IndexMethod("Table").Item1;

            Assert.NotNull(func);
            var parameters = func.Parameters;
            Assert.Equal(1, parameters.Count());

            ParameterDescriptor firstParameter = parameters.First();
            Assert.NotNull(firstParameter);
            Assert.Equal("table", firstParameter.Name);
            Assert.IsType<TableParameterDescriptor>(firstParameter);
            TableParameterDescriptor typedTableParameter = (TableParameterDescriptor)firstParameter;
            Assert.Equal("TableName", typedTableParameter.TableName);
        }

        public static void QueueTrigger([QueueTrigger("inputQueue")] int queueValue) { }

        [Fact]
        public void TestQueueTrigger()
        {
            FunctionDescriptor func = IndexMethod("QueueTrigger").Item1;

            Assert.NotNull(func);
            var parameters = func.Parameters;
            Assert.Equal(1, parameters.Count());

            ParameterDescriptor firstParameter = parameters.First();
            Assert.IsType<QueueTriggerParameterDescriptor>(firstParameter);
            QueueTriggerParameterDescriptor queueParameter = (QueueTriggerParameterDescriptor)firstParameter;
            Assert.Equal("inputqueue", queueParameter.QueueName); // queue name gets normalized. 
            Assert.Equal("queueValue", firstParameter.Name); // parameter name does not.
        }

        // Queue inputs with implicit names.
        public static void QueueOutput([Queue("inputQueue")] out string inputQueue)
        {
            inputQueue = "0";
        }

        [Fact]
        public void TestQueueOutput()
        {
            FunctionDescriptor func = IndexMethod("QueueOutput").Item1;

            Assert.NotNull(func);
            var parameters = func.Parameters;
            Assert.Equal(1, parameters.Count());

            ParameterDescriptor firstParameter = parameters.First();
            QueueParameterDescriptor queueParameter = (QueueParameterDescriptor)firstParameter;
            Assert.Equal("inputqueue", queueParameter.QueueName); // queue name gets normalized.
            Assert.Equal("inputQueue", firstParameter.Name); // parameter name does not.
        }

        // Has an unbound parameter, so this will require an explicit invoke.  
        // Trigger: NoListener, explicit
        [NoAutomaticTrigger]
        public static void HasBlobAndUnboundParameter([BlobTrigger("container")] Stream input, int unbound) { }

        [Fact]
        public void TestHasBlobAndUnboundParameter()
        {
            var entry = IndexMethod("HasBlobAndUnboundParameter");

            Assert.NotNull(entry);
            
            IFunctionDefinition definiton = entry.Item2;
            Assert.NotNull(definiton);
            Assert.Null(definiton.ListenerFactory);

            FunctionDescriptor descriptor = entry.Item1;
            Assert.NotNull(descriptor);
            var parameters = descriptor.Parameters;
            Assert.Equal(2, parameters.Count());

            ParameterDescriptor firstParameter = parameters.ElementAt(0);
            Assert.Equal("input", firstParameter.Name);
            Assert.IsType<BlobTriggerParameterDescriptor>(firstParameter);
            BlobTriggerParameterDescriptor blobParameter = (BlobTriggerParameterDescriptor)firstParameter;
            Assert.Equal("container", blobParameter.ContainerName);

            ParameterDescriptor secondParameter = parameters.ElementAt(1);
            Assert.Equal("unbound", secondParameter.Name);
            Assert.IsType<CallerSuppliedParameterDescriptor>(secondParameter);
        }

        // Both parameters are bound. 
        // Trigger: Automatic listener
        public static void HasBlobAndBoundParameter([BlobTrigger(@"container/{bound}")] Stream input, int bound) { }

        [Fact]
        public void TestHasBlobAndBoundParameter()
        {
            FunctionDescriptor func = IndexMethod("HasBlobAndBoundParameter").Item1;

            Assert.NotNull(func);
            var parameters = func.Parameters;
            Assert.Equal(2, parameters.Count());

            ParameterDescriptor firstParameter = parameters.ElementAt(0);
            Assert.Equal("input", firstParameter.Name);
            Assert.IsType<BlobTriggerParameterDescriptor>(firstParameter);
            BlobTriggerParameterDescriptor blobParameter = (BlobTriggerParameterDescriptor)firstParameter;
            Assert.Equal("container", blobParameter.ContainerName);

            ParameterDescriptor secondParameter = parameters.ElementAt(1);
            Assert.Equal("bound", secondParameter.Name);
            Assert.IsType<BindingDataParameterDescriptor>(secondParameter);
        }
    }
}
