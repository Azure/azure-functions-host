// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.ServiceBus;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ServiceBusScriptBindingProviderTests
    {
        private readonly GeneralScriptBindingProvider _provider;

        public ServiceBusScriptBindingProviderTests()
        {
            var serviceBusConfig = new ServiceBusConfiguration();
            serviceBusConfig.MessagingProvider = new MessagingProvider(serviceBusConfig);

            JobHostConfiguration config = new JobHostConfiguration();
            config.UseServiceBus(serviceBusConfig);
            JObject hostMetadata = new JObject();
            _provider = new GeneralScriptBindingProvider(config, hostMetadata, null);
            var metadataProvider = new JobHost(config).CreateMetadataProvider();
            _provider.CompleteInitialization(metadataProvider);
        }

        [Fact]
        public void TryCreate_GetAttributes_EntityTypeQueue()
        {
            JObject bindingMetadata = new JObject
            {
                { "type", "serviceBus" },
                { "name", "test" },
                { "direction", "out" },
                { "queueName", "queue" }
            };

            ScriptBindingContext context = new ScriptBindingContext(bindingMetadata);
            ScriptBinding binding = null;
            bool created = _provider.TryCreate(context, out binding);

            Assert.True(created);
            Assert.Same(binding.Context, context);

            var serviceBusAttr = binding.GetAttributes()
                .Where(attr => attr is ServiceBusAttribute)
                .First() as ServiceBusAttribute;

            Assert.Equal(EntityType.Queue, serviceBusAttr.EntityType);
        }

        [Fact]
        public void TryCreate_GetAttributes_EntityTypeTopic()
        {
            JObject bindingMetadata = new JObject
            {
                { "type", "serviceBus" },
                { "name", "test" },
                { "direction", "out" },
                { "topicName", "topic" }
            };
            ScriptBindingContext context = new ScriptBindingContext(bindingMetadata);
            ScriptBinding binding = null;
            bool created = _provider.TryCreate(context, out binding);

            Assert.True(created);
            Assert.Same(binding.Context, context);

            var serviceBusAttr = binding.GetAttributes()
                .Where(attr => attr is ServiceBusAttribute)
                .First() as ServiceBusAttribute;

            Assert.Equal(EntityType.Topic, serviceBusAttr.EntityType);
        }

        [Fact]
        public void TryCreate_GetAttributes_EntityTypeQueue_Trigger()
        {
            JObject bindingMetadata = new JObject
            {
                { "type", "serviceBusTrigger" },
                { "name", "test" },
                { "direction", "in" },
                { "queueName", "queue" }
            };

            ScriptBindingContext context = new ScriptBindingContext(bindingMetadata);
            ScriptBinding binding = null;
            bool created = _provider.TryCreate(context, out binding);

            Assert.True(created);
            Assert.Same(binding.Context, context);

            var serviceBusTriggerAttr = binding.GetAttributes()
                .Where(attr => attr is ServiceBusTriggerAttribute)
                .First() as ServiceBusTriggerAttribute;

            Assert.Equal(serviceBusTriggerAttr.QueueName, "queue");
        }

        [Fact]
        public void TryCreate_GetAttributes_EntityTypeTopic_Trigger()
        {
            JObject bindingMetadata = new JObject
            {
                { "type", "serviceBusTrigger" },
                { "name", "test" },
                { "direction", "in" },
                { "topicName", "topic" },
                { "subscriptionName", "testSub" }
            };
            ScriptBindingContext context = new ScriptBindingContext(bindingMetadata);
            ScriptBinding binding = null;
            bool created = _provider.TryCreate(context, out binding);

            Assert.True(created);
            Assert.Same(binding.Context, context);

            var serviceBusTriggerAttr = binding.GetAttributes()
                .Where(attr => attr is ServiceBusTriggerAttribute)
                .First() as ServiceBusTriggerAttribute;

            Assert.Equal(serviceBusTriggerAttr.TopicName, "topic");
        }
    }
}