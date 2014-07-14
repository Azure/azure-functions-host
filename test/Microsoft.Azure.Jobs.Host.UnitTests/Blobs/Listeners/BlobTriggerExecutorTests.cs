using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Blobs;
using Microsoft.Azure.Jobs.Host.Blobs.Listeners;
using Microsoft.WindowsAzure.Storage.Blob;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Blobs.Listeners
{
    public class BlobTriggerExecutorTests
    {
        // Sequential times.
        static DateTime TimeOld = new DateTime(1900, 1, 1);
        static DateTime TimeMiddle = new DateTime(1900, 1, 2);
        static DateTime TimeNew = new DateTime(1900, 1, 3);

        [Fact]
        public void NoOutputs()
        {
            var input = CreateInput("container", "skip");
            IEnumerable<IBindableBlobPath> outputs = null;

            bool invoke = ShouldInvokeTrigger(input, outputs);

            Assert.True(invoke);
        }

        [Fact]
        public void NoOutputsEmptyArray()
        {
            var input = CreateInput("container", "skip");
            var outputs = new IBindableBlobPath[0];

            bool invoke = ShouldInvokeTrigger(input, outputs);

            Assert.True(invoke);
        }

        [Fact]
        public void NewerInput()
        {
            var input = CreateInput("container", "new");
            var outputs = new IBindableBlobPath[]
            {
                CreateOutput("container", "old")
            };

            bool invoke = ShouldInvokeTrigger(input, outputs);

            Assert.True(invoke);
        }

        [Fact]
        public void MissingOutput()
        {
            var input = CreateInput("container", "old");
            var outputs = new IBindableBlobPath[]
            {
                CreateOutput("container", "missing")
            };

            bool invoke = ShouldInvokeTrigger(input, outputs);

            Assert.True(invoke);
        }

        [Fact]
        public void OlderInput()
        {
            var input = CreateInput("container", "old");
            var outputs = new IBindableBlobPath[]
            {
                CreateOutput("container", "new")
            };

            bool invoke = ShouldInvokeTrigger(input, outputs);

            Assert.False(invoke);
        }

        [Fact]
        public void StrippedInput()
        {
            // Input is newer than one of the outputs
            var input = CreateInput("container", "middle");
            var outputs = new IBindableBlobPath[]
            {
                CreateOutput("container", "old"),
                CreateOutput("container", "new")
            };

            bool invoke = ShouldInvokeTrigger(input, outputs);

            Assert.True(invoke);
        }

        bool ShouldInvokeTrigger(BlobPath input, IEnumerable<IBindableBlobPath> outputs)
        {
            var nvc = new Dictionary<string, object>();
            var inputTime = LookupTime(input);
            Assert.True(inputTime.HasValue);
            CloudBlobClient client = new CloudBlobClient(new Uri("http://ignore"));
            CloudBlobContainer inputContainer = client.GetContainerReference(input.ContainerName);
            ICloudBlob possibleTrigger = inputContainer.GetBlockBlobReference(input.BlobName);

            return BlobTriggerExecutor.ShouldExecuteTrigger(possibleTrigger, new FixedBlobPathSource(input), outputs,
                new LambdaTimestampReader(LookupTime));
        }

        private static BlobPath CreateInput(string containerName, string blobName)
        {
            return new BlobPath(containerName, blobName);
        }

        private static IBindableBlobPath CreateOutput(string containerName, string blobName)
        {
            return new BoundBlobPath(new BlobPath(containerName, blobName));
        }

        private static DateTime? LookupTime(ICloudBlob blob)
        {
            return LookupTime(new BlobPath(blob.Container.Name, blob.Name));
        }

        private static DateTime? LookupTime(BlobPath path)
        {
            if (path.BlobName.EndsWith("missing"))
            {
                return null;
            }
            switch (path.BlobName)
            {
                case "skip": return TimeOld;
                case "old": return TimeOld;
                case "middle": return TimeMiddle;
                case "new": return TimeNew;
            }
            throw new InvalidOperationException("Unexpected blob name: " + path.ToString());
        }

        private class LambdaTimestampReader : IBlobTimestampReader
        {
            private readonly Func<ICloudBlob, DateTime?> _getLastModifiedTimestamp;

            public LambdaTimestampReader(Func<ICloudBlob, DateTime?> getLastModifiedTimestamp)
            {
                _getLastModifiedTimestamp = getLastModifiedTimestamp;
            }

            public DateTime? GetLastModifiedTimestamp(ICloudBlob blob)
            {
                return _getLastModifiedTimestamp.Invoke(blob);
            }
        }
    }
}
