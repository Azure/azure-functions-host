using System;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.UnitTests
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
                 BlobInput = new CloudBlobPath("container", "skip")                  
            };

            var nvc = new Dictionary<string,string>();
            bool invoke = Listener.ShouldInvokeTrigger(trigger, nvc, TimeOld, LookupTime);
            Assert.True(invoke);
        }

        [Fact]
        public void NoOutputsEmptyArray()
        {
            var trigger = new BlobTrigger
            {
                BlobInput = new CloudBlobPath("container", "skip"),
                BlobOutputs = new CloudBlobPath[0]
            };

            var nvc = new Dictionary<string, string>();
            bool invoke = Listener.ShouldInvokeTrigger(trigger, nvc, TimeOld, LookupTime);
            Assert.True(invoke);
        }
         
        [Fact]
        public void NewerInput()
        {
            var trigger = new BlobTrigger
            {
                BlobInput = new CloudBlobPath("container", "new"),
                BlobOutputs = new CloudBlobPath[] {
                    new CloudBlobPath("container", "old")
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
                BlobInput = new CloudBlobPath("container", "old"),
                BlobOutputs = new CloudBlobPath[] {
                    new CloudBlobPath("container", "missing")
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
                BlobInput = new CloudBlobPath("container", "old"),
                BlobOutputs = new CloudBlobPath[] {
                    new CloudBlobPath("container", "new")
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
                BlobInput = new CloudBlobPath("container", "middle"),
                BlobOutputs = new CloudBlobPath[] {
                    new CloudBlobPath("container", "old"),
                    new CloudBlobPath("container", "new")
                }
            };

            bool invoke = ShouldInvokeTrigger(trigger);

            Assert.True(invoke);
        }


        bool ShouldInvokeTrigger(BlobTrigger trigger)
        {
            var nvc = new Dictionary<string, string>();
            var inputTime = LookupTime(trigger.BlobInput);
            Assert.True(inputTime.HasValue);

            bool invoke = Listener.ShouldInvokeTrigger(trigger, nvc, inputTime.Value, LookupTime);
            return invoke;
        }

        internal static DateTime? LookupTime(CloudBlobPath path)
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
