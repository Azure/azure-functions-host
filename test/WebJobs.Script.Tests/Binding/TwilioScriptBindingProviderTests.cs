// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#if ALLEXTENSIONS
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TwilioScriptBindingProviderTests
    {
        private readonly TwilioScriptBindingProvider _provider;

        public TwilioScriptBindingProviderTests()
        {
            JobHostConfiguration config = new JobHostConfiguration();
            TestTraceWriter traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            JObject hostMetadata = new JObject();
            _provider = new TwilioScriptBindingProvider(config, hostMetadata, traceWriter);
        }

        [Fact]
        public void TryCreate_MatchingMetadata_CreatesBinding()
        {
            JObject bindingMetadata = new JObject
            {
                { "type", "twilioSMS" },
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
#endif