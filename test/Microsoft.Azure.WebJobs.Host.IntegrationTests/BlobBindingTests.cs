// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.WebJobs.Host.IntegrationTests
{
    public class BlobBindingTests : IDisposable
    {
        internal const string TestContainerName = "daas-test-input";
        internal const string TestBlobName = "blob.csv";
        internal const string TestBlobPath = TestContainerName + "/" + TestBlobName;
        internal const int TestValue = Int32.MinValue;

        [Theory]
        [InlineData("FuncWithString")]
        [InlineData("FuncWithTextReader")]
        [InlineData("FuncWithStreamRead")]
        [InlineData("FuncWithBlockBlob")]
        [InlineData("FuncWithOutStringNull")]
        [InlineData("FuncWithStreamWriteNoop")]
        [InlineData("FuncWithT")]
        [InlineData("FuncWithValueT")]
        public void Call_WhenMissingBlob_DoesntCreate(string functionName)
        {
            CloudStorageAccount account = TestStorage.GetAccount();
            var lc = TestStorage.New<MissingBlobProgram>(account, new Type[] { 
                typeof(MissingBlobToCustomValueBinder), typeof(MissingBlobToCustomObjectBinder) });
            Assert.False(TestBlobClient.DoesBlobExist(account, TestContainerName, TestBlobName),
                "blob should NOT exist before the test.");

            lc.Call(functionName);

            Assert.False(TestBlobClient.DoesBlobExist(account, TestContainerName, TestBlobName),
                "blob should NOT be created when nothing is written to a stream or " +
                "null value is returned by the function.");
        }

        [Theory]
        [InlineData("FuncWithOutString")]
        [InlineData("FuncWithTextWriter")]
        [InlineData("FuncWithStreamWrite")]
        [InlineData("FuncWithOutT")]
        [InlineData("FuncWithOutTNull")]
        [InlineData("FuncWithOutValueT")]
        public void Call_WhenMissingBlob_Creates(string functionName)
        {
            CloudStorageAccount account = TestStorage.GetAccount();
            var lc = TestStorage.New<MissingBlobProgram>(account, new Type[] { 
                typeof(MissingBlobToCustomValueBinder), typeof(MissingBlobToCustomObjectBinder) });
            Assert.False(TestBlobClient.DoesBlobExist(account, TestContainerName, TestBlobName),
                "blob should NOT exist before the test.");

            lc.Call(functionName);

            Assert.True(TestBlobClient.DoesBlobExist(account, TestContainerName, TestBlobName),
                "blob must be created if a function wrote to a stream or returned non null value.");
        }

        public void Dispose()
        {
            CloudStorageAccount account = TestStorage.GetAccount();
            TestBlobClient.DeleteContainer(account, TestContainerName);
            Assert.False(TestBlobClient.DoesBlobExist(account, TestContainerName, TestBlobName), 
                "blob should be deleted after Dispose");
        }

        private struct CustomDataValue
        {
            public int ValueId { get; set; }
            public string Content { get; set; }
        }

        private class CustomDataObject
        {
            public int ValueId { get; set; }
            public string Content { get; set; }
        }

        private class MissingBlobToCustomValueBinder : ICloudBlobStreamBinder<CustomDataValue>
        {
            public Task<CustomDataValue> ReadFromStreamAsync(Stream input, CancellationToken cancellationToken)
            {
                Assert.Null(input);

                CustomDataValue value = new CustomDataValue { ValueId = TestValue };
                return Task.FromResult(value);
            }

            public Task WriteToStreamAsync(CustomDataValue value, Stream output, CancellationToken cancellationToken)
            {
                Assert.NotNull(output);

                Assert.Equal(TestValue, value.ValueId);

                const byte ignore = 0xFF;
                output.WriteByte(ignore);
                
                return Task.FromResult(0);
            }
        }

        private class MissingBlobToCustomObjectBinder : ICloudBlobStreamBinder<CustomDataObject>
        {
            public Task<CustomDataObject> ReadFromStreamAsync(Stream input, CancellationToken cancellationToken)
            {
                Assert.Null(input);

                CustomDataObject value = new CustomDataObject { ValueId = TestValue };
                return Task.FromResult(value);
            }

            public Task WriteToStreamAsync(CustomDataObject value, Stream output, CancellationToken cancellationToken)
            {
                Assert.NotNull(output);

                if (value != null)
                {
                    Assert.Equal(TestValue, value.ValueId);

                    const byte ignore = 0xFF;
                    output.WriteByte(ignore);
                }

                return Task.FromResult(0);
            }
        }

        private class MissingBlobProgram
        {
            public static void FuncWithBlockBlob([Blob(TestBlobPath)] CloudBlockBlob blob)
            {
                Assert.NotNull(blob);
                Assert.Equal(TestBlobName, blob.Name);
                Assert.Equal(TestContainerName, blob.Container.Name);
            }

            public static void FuncWithStreamRead([Blob(TestBlobPath, FileAccess.Read)] Stream stream)
            {
                Assert.Null(stream);
            }

            public static void FuncWithStreamWrite([Blob(TestBlobPath, FileAccess.Write)] Stream stream)
            {
                Assert.NotNull(stream);

                const byte ignore = 0xFF;
                stream.WriteByte(ignore);
            }

            public static void FuncWithStreamWriteNoop([Blob(TestBlobPath, FileAccess.Write)] Stream stream)
            {
                Assert.NotNull(stream);
            }

            public static void FuncWithTextReader([Blob(TestBlobPath)] TextReader reader)
            {
                Assert.Null(reader);
            }

            public static void FuncWithTextWriter([Blob(TestBlobPath)] TextWriter writer)
            {
                Assert.NotNull(writer);
            }

            public static void FuncWithString([Blob(TestBlobPath)] string content)
            {
                Assert.Null(content);
            }

            public static void FuncWithOutString([Blob(TestBlobPath)] out string content)
            {
                content = "ignore";
            }

            public static void FuncWithOutStringNull([Blob(TestBlobPath)] out string content)
            {
                content = null;
            }

            public static void FuncWithT([Blob(TestBlobPath)] CustomDataObject value)
            {
                Assert.NotNull(value);
                Assert.Equal(TestValue, value.ValueId);
            }

            public static void FuncWithOutT([Blob(TestBlobPath)] out CustomDataObject value)
            {
                value = new CustomDataObject { ValueId = TestValue, Content = "ignore" };
            }

            public static void FuncWithOutTNull([Blob(TestBlobPath)] out CustomDataObject value)
            {
                value = null;
            }

            public static void FuncWithValueT([Blob(TestBlobPath)] CustomDataValue value)
            {
                Assert.NotNull(value);
                Assert.Equal(TestValue, value.ValueId);
            }

            public static void FuncWithOutValueT([Blob(TestBlobPath)] out CustomDataValue value)
            {
                value = new CustomDataValue { ValueId = TestValue, Content = "ignore" };
            }
        }
    }
}
