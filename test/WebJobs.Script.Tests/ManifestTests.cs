// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Script;
using Newtonsoft.Json.Linq;
using Xunit;

namespace WebJobs.Script.Tests
{
    public class ManifestTests
    {
        private const string ID = "5a709861cab44e68bfed5d2c2fe7fc0c";

        [Fact]
        public void ApplyConfiguration_Default()
        {
            JObject manifest = new JObject();
            manifest["id"] = ID;
            JobHostConfiguration config = new JobHostConfiguration();

            Manifest.ApplyConfiguration(manifest, config);
            Assert.Equal(ID, config.HostId);
        }

        [Fact]
        public void ApplyConfiguration_Queues()
        {
            JObject manifest = new JObject();
            manifest["id"] = ID;
            JObject queuesConfig = new JObject();
            manifest["queues"] = queuesConfig;
            JobHostConfiguration config = new JobHostConfiguration();

            // verify empty config
            Manifest.ApplyConfiguration(manifest, config);
            Assert.Equal(ID, config.HostId);

            Assert.Equal(60 * 1000, config.Queues.MaxPollingInterval.TotalMilliseconds);
            Assert.Equal(16, config.Queues.BatchSize);
            Assert.Equal(5, config.Queues.MaxDequeueCount);
            Assert.Equal(8, config.Queues.NewBatchThreshold);

            queuesConfig["maxPollingInterval"] = 5000;
            queuesConfig["batchSize"] = 17;
            queuesConfig["maxDequeueCount"] = 3;
            queuesConfig["newBatchThreshold"] = 123;

            // set all the knobs and verify
            Manifest.ApplyConfiguration(manifest, config);
            Assert.Equal(5000, config.Queues.MaxPollingInterval.TotalMilliseconds);
            Assert.Equal(17, config.Queues.BatchSize);
            Assert.Equal(3, config.Queues.MaxDequeueCount);
            Assert.Equal(123, config.Queues.NewBatchThreshold);
        }

        [Fact]
        public void ApplyConfiguration_Singleton()
        {
            JObject manifest = new JObject();
            manifest["id"] = ID;
            JObject singleton = new JObject();
            manifest["singleton"] = singleton;
            JobHostConfiguration config = new JobHostConfiguration();

            // verify empty config
            Manifest.ApplyConfiguration(manifest, config);
            Assert.Equal(ID, config.HostId);

            Assert.Equal(15, config.Singleton.LockPeriod.TotalSeconds);
            Assert.Equal(60, config.Singleton.ListenerLockPeriod.TotalSeconds);
            Assert.Equal(1, config.Singleton.ListenerLockRecoveryPollingInterval.TotalMinutes);
            Assert.Equal(1, config.Singleton.LockAcquisitionTimeout.TotalMinutes);
            Assert.Equal(3, config.Singleton.LockAcquisitionPollingInterval.TotalSeconds);

            singleton["lockPeriod"] = "00:00:17";
            singleton["listenerLockPeriod"] = "00:00:22";
            singleton["listenerLockRecoveryPollingInterval"] = "00:00:33";
            singleton["lockAcquisitionTimeout"] = "00:05:00";
            singleton["lockAcquisitionPollingInterval"] = "00:00:08";

            // set all the knobs and verify
            Manifest.ApplyConfiguration(manifest, config);
            Assert.Equal(17, config.Singleton.LockPeriod.TotalSeconds);
            Assert.Equal(22, config.Singleton.ListenerLockPeriod.TotalSeconds);
            Assert.Equal(33, config.Singleton.ListenerLockRecoveryPollingInterval.TotalSeconds);
            Assert.Equal(5, config.Singleton.LockAcquisitionTimeout.TotalMinutes);
            Assert.Equal(8, config.Singleton.LockAcquisitionPollingInterval.TotalSeconds);
        }
    }
}
