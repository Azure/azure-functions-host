// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
#if ALLEXTENSIONS
using System;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Azure.WebJobs.Extensions.SendGrid;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SendGridScriptBindingProviderTests
    {
        private readonly ScriptBindingProvider _provider;

        public SendGridScriptBindingProviderTests()
        {
            JobHostConfiguration config = new JobHostConfiguration();
            config.AddExtension(new SendGridConfiguration());
            TestTraceWriter traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            JObject hostMetadata = new JObject();

            var provider = new GeneralScriptBindingProvider(config, hostMetadata, traceWriter);
            var metadataProvider = new JobHost(config).CreateMetadataProvider();
            provider.CompleteInitialization(metadataProvider);
            _provider = provider;
        }

        private static SendGridConfiguration CreateConfiguration(JObject config)
        {
            var ctx = new ExtensionConfigContext
            {
                Config = new JobHostConfiguration()
                {
                    HostConfigMetadata = config
                },
                Trace = new TestTraceWriter(TraceLevel.Verbose)
            };
            SendGridConfiguration result = new SendGridConfiguration();
            result.Initialize(ctx);

            return result;
        }

        [Fact]
        public void CreateConfiguration_CreatesExpectedConfiguration()
        {
            JObject config = new JObject();

            var result = CreateConfiguration(config);

            Assert.Null(result.FromAddress);
            Assert.Null(result.ToAddress);

            config = new JObject
            {
                {
                    "sendGrid", new JObject
                    {
                        { "to", "Testing1 <test1@test.com>" },
                        { "from", "Testing2 <test2@test.com>" }
                    }
                }
            };
            result = CreateConfiguration(config);

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
        public void TryResolveAssemblies()
        {
            // Verify that we can resolve references to the native SendGrid SDK.
            Assembly expectedAssembly = typeof(SendGrid.SendGridAPIClient).Assembly;
            Assembly assembly;
            bool resolved = _provider.TryResolveAssembly(expectedAssembly.GetName().FullName, out assembly);
            Assert.True(resolved);
            Assert.Same(expectedAssembly, assembly);

            resolved = _provider.TryResolveAssembly(expectedAssembly.GetName().Name, out assembly);
            Assert.True(resolved);
            Assert.Same(expectedAssembly, assembly);
        }

        [Fact]
        public void TryCreate_NoMatchingMetadata_DoesNotCreateBinding()
        {
            JObject bindingMetadata = new JObject
            {
                { "type", "unknown" },
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