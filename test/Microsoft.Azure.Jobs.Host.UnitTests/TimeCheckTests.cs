using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Blobs;
using Microsoft.Azure.Jobs.Host.Blobs.Bindings;
using Microsoft.Azure.Jobs.Host.Blobs.Triggers;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    public class TimeCheckTests
    {
        // Sequential times.
        static DateTime TimeOld = new DateTime(1900, 1, 1);
        static DateTime TimeMiddle = new DateTime(1900, 1, 2);
        static DateTime TimeNew = new DateTime(1900, 1, 3);

        [Fact]
        public void NoOutputs()
        {
            var trigger = new BlobTrigger {
                 BlobInput = CreateBlobPathSource("container", "skip")
            };

            var nvc = new Dictionary<string,object>();
            bool invoke = Listener.ShouldInvokeTrigger(trigger, nvc, TimeOld, LookupTime);
            Assert.True(invoke);
        }

        [Fact]
        public void NoOutputsEmptyArray()
        {
            var trigger = new BlobTrigger
            {
                BlobInput = CreateBlobPathSource("container", "skip"),
                BlobOutputs = new IBindableBlobPath[0]
            };

            var nvc = new Dictionary<string, object>();
            bool invoke = Listener.ShouldInvokeTrigger(trigger, nvc, TimeOld, LookupTime);
            Assert.True(invoke);
        }
         
        [Fact]
        public void NewerInput()
        {
            var trigger = new BlobTrigger
            {
                BlobInput = CreateBlobPathSource("container", "new"),
                BlobOutputs = new IBindableBlobPath[] {
                    new BoundBlobPath(new BlobPath("container", "old"))
                }
            };

            bool invoke = ShouldInvokeTrigger(trigger);

            Assert.True(invoke);
        }

        [Fact]
        public void MissingOutput()
        {
            var trigger = new BlobTrigger
            {
                BlobInput = CreateBlobPathSource("container", "old"),
                BlobOutputs = new IBindableBlobPath[] {
                    CreateBindableBlobPath("container", "missing")
                }
            };

            bool invoke = ShouldInvokeTrigger(trigger);

            Assert.True(invoke);
        }

        [Fact]
        public void OlderInput()
        {
            var trigger = new BlobTrigger
            {
                BlobInput = CreateBlobPathSource("container", "old"),
                BlobOutputs = new IBindableBlobPath[] {
                    CreateBindableBlobPath("container", "new")
                }
            };

            bool invoke = ShouldInvokeTrigger(trigger);

            Assert.False(invoke);
        }

        [Fact]
        public void StrippedInput()
        {
            // Input is new than one of the outputs
            var trigger = new BlobTrigger
            {
                BlobInput = CreateBlobPathSource("container", "middle"),
                BlobOutputs = new IBindableBlobPath[] {
                    CreateBindableBlobPath("container", "old"),
                    CreateBindableBlobPath("container", "new")
                }
            };

            bool invoke = ShouldInvokeTrigger(trigger);

            Assert.True(invoke);
        }

        bool ShouldInvokeTrigger(BlobTrigger trigger)
        {
            var nvc = new Dictionary<string, object>();
            var inputTime = LookupTime(trigger.BlobInput);
            Assert.True(inputTime.HasValue);

            bool invoke = Listener.ShouldInvokeTrigger(trigger, nvc, inputTime.Value, LookupTime);
            return invoke;
        }

        private static IBindableBlobPath CreateBindableBlobPath(string containerName, string blobName)
        {
            return new BoundBlobPath(new BlobPath(containerName, blobName));
        }

        private static IBlobPathSource CreateBlobPathSource(string containerName, string blobName)
        {
            return new FixedBlobPathSource(new BlobPath(containerName, blobName));
        }

        internal static DateTime? LookupTime(IBlobPathSource path)
        {
            return LookupTime(new BlobPath(path.ContainerNamePattern, path.BlobNamePattern));
        }

        internal static DateTime? LookupTime(BlobPath path)
        {
            if (path.BlobName.EndsWith("missing"))
            {
                return null;
            }
            switch (path.BlobName)
            {
                case "old": return TimeOld;
                case "middle": return TimeMiddle;
                case "new": return TimeNew;
            }
            throw new InvalidOperationException("Unexpected blob name: " + path.ToString());
        }
    }
}
