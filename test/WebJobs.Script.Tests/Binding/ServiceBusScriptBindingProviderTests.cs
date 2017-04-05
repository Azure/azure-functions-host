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
        private readonly ServiceBusScriptBindingProvider _provider;

        public ServiceBusScriptBindingProviderTests()
        {
            JobHostConfiguration config = new JobHostConfiguration();
            TestTraceWriter traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            JObject hostMetadata = new JObject();
            _provider = new ServiceBusScriptBindingProvider(config, hostMetadata, traceWriter);
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
    }
}
