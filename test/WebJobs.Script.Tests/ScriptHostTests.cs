// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Script;
using Newtonsoft.Json.Linq;
using Xunit;

namespace WebJobs.Script.Tests
{
    public class ScriptHostTests
    {
        private const string ID = "5a709861cab44e68bfed5d2c2fe7fc0c";

        [Fact]
        public void ApplyConfiguration_TopLevel()
        {
            JObject config = new JObject();
            config["id"] = ID;
            JobHostConfiguration jobHostConfig = new JobHostConfiguration();

            ScriptHost.ApplyConfiguration(config, jobHostConfig);

            Assert.Equal(ID, jobHostConfig.HostId);
        }

        [Fact]
        public void ApplyConfiguration_Queues()
        {
            JObject config = new JObject();
            config["id"] = ID;
            JObject queuesConfig = new JObject();
            config["queues"] = queuesConfig;
            JobHostConfiguration jobHostConfig = new JobHostConfiguration();

            ScriptHost.ApplyConfiguration(config, jobHostConfig);

            Assert.Equal(ID, jobHostConfig.HostId);
            Assert.Equal(60 * 1000, jobHostConfig.Queues.MaxPollingInterval.TotalMilliseconds);
            Assert.Equal(16, jobHostConfig.Queues.BatchSize);
            Assert.Equal(5, jobHostConfig.Queues.MaxDequeueCount);
            Assert.Equal(8, jobHostConfig.Queues.NewBatchThreshold);

            queuesConfig["maxPollingInterval"] = 5000;
            queuesConfig["batchSize"] = 17;
            queuesConfig["maxDequeueCount"] = 3;
            queuesConfig["newBatchThreshold"] = 123;

            ScriptHost.ApplyConfiguration(config, jobHostConfig);

            Assert.Equal(5000, jobHostConfig.Queues.MaxPollingInterval.TotalMilliseconds);
            Assert.Equal(17, jobHostConfig.Queues.BatchSize);
            Assert.Equal(3, jobHostConfig.Queues.MaxDequeueCount);
            Assert.Equal(123, jobHostConfig.Queues.NewBatchThreshold);
        }

        [Fact]
        public void ApplyConfiguration_Singleton()
        {
            JObject config = new JObject();
            config["id"] = ID;
            JObject singleton = new JObject();
            config["singleton"] = singleton;
            JobHostConfiguration jobHostConfig = new JobHostConfiguration();

            ScriptHost.ApplyConfiguration(config, jobHostConfig);

            Assert.Equal(ID, jobHostConfig.HostId);
            Assert.Equal(15, jobHostConfig.Singleton.LockPeriod.TotalSeconds);
            Assert.Equal(1, jobHostConfig.Singleton.ListenerLockPeriod.TotalMinutes);
            Assert.Equal(1, jobHostConfig.Singleton.ListenerLockRecoveryPollingInterval.TotalMinutes);
            Assert.Equal(1, jobHostConfig.Singleton.LockAcquisitionTimeout.TotalMinutes);
            Assert.Equal(3, jobHostConfig.Singleton.LockAcquisitionPollingInterval.TotalSeconds);

            singleton["lockPeriod"] = "00:00:17";
            singleton["listenerLockPeriod"] = "00:00:22";
            singleton["listenerLockRecoveryPollingInterval"] = "00:00:33";
            singleton["lockAcquisitionTimeout"] = "00:05:00";
            singleton["lockAcquisitionPollingInterval"] = "00:00:08";

            ScriptHost.ApplyConfiguration(config, jobHostConfig);

            Assert.Equal(17, jobHostConfig.Singleton.LockPeriod.TotalSeconds);
            Assert.Equal(22, jobHostConfig.Singleton.ListenerLockPeriod.TotalSeconds);
            Assert.Equal(33, jobHostConfig.Singleton.ListenerLockRecoveryPollingInterval.TotalSeconds);
            Assert.Equal(5, jobHostConfig.Singleton.LockAcquisitionTimeout.TotalMinutes);
            Assert.Equal(8, jobHostConfig.Singleton.LockAcquisitionPollingInterval.TotalSeconds);
        }

        [Fact]
        public void ApplyConfiguration_Tracing()
        {
            JObject config = new JObject();
            config["id"] = ID;
            JObject tracing = new JObject();
            config["tracing"] = tracing;
            JobHostConfiguration jobHostConfig = new JobHostConfiguration();

            Assert.Equal(TraceLevel.Info, jobHostConfig.Tracing.ConsoleLevel);

            tracing["consoleLevel"] = "Verbose";

            ScriptHost.ApplyConfiguration(config, jobHostConfig);
            Assert.Equal(TraceLevel.Verbose, jobHostConfig.Tracing.ConsoleLevel);
        }
    }
}
