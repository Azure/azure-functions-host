// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SendGridScriptBindingProviderTests
    {
        private readonly SendGridScriptBindingProvider _provider;

        public SendGridScriptBindingProviderTests()
        {
            JobHostConfiguration config = new JobHostConfiguration();
            TestTraceWriter traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            JObject hostMetadata = new JObject();
            _provider = new SendGridScriptBindingProvider(config, hostMetadata, traceWriter);
        }

        [Fact]
        public void CreateConfiguration_CreatesExpectedConfiguration()
        {
            JObject config = new JObject();
            var result = SendGridScriptBindingProvider.CreateConfiguration(config);

            Assert.Null(result.FromAddress);
            Assert.Null(result.ToAddress);

            config = new JObject
            {
                { "sendGrid", new JObject
                    {
                        { "to", "Testing1 <test1@test.com>" },
                        { "from", "Testing2 <test2@test.com>" }
                    }
                }
            };
            result = SendGridScriptBindingProvider.CreateConfiguration(config);

            Assert.Equal("test1@test.com", result.ToAddress.Address);
            Assert.Equal("Testing1", result.ToAddress.Name);
            Assert.Equal("test2@test.com", result.FromAddress.Address);
            Assert.Equal("Testing2", result.FromAddress.Name);
        }

        [Fact]
        public void TryCreate_MatchingMetadata_CreatesBinding()
        {
            JObject bindingMetadata = new JObject
            {
                { "type", "sendGrid" },
                { "name", "test" },
                { "direction", "out" }
            };
            ScriptBindingContext context = new ScriptBindingContext(bindingMetadata);
            ScriptBinding binding = null;
            bool created = _provider.TryCreate(context, out binding);

            Assert.True(created);
            Assert.Same(binding.Context, context);
            Assert.Same(typeof(IAsyncCollector<JObject>), binding.DefaultType);
        }

        [Fact]
        public void TryCreate_NoMatchingMetadata_DoesNotCreateBinding()
        {
            JObject bindingMetadata = new JObject
            {
                { "type", "queue" },
                { "name", "test" },
                { "direction", "out" }
            };
            ScriptBindingContext context = new ScriptBindingContext(bindingMetadata);
            ScriptBinding binding = null;
            bool created = _provider.TryCreate(context, out binding);

            Assert.False(created);
            Assert.Null(binding);
        }
    }
}
