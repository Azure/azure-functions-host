// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ExtensionSharedAssemblyProviderTests
    {
        [Fact]
        public void TryResolveAssembly_ResolvesProviderAssembly()
        {
            var bindingProviders = new Collection<ScriptBindingProvider>
            {
                new TestBindingProvider(new JobHostConfiguration(), new JObject(), null)
            };

            var provider = new ExtensionSharedAssemblyProvider(bindingProviders);

            Assembly assembly;
            bool result = provider.TryResolveAssembly(typeof(TestBindingProvider).Assembly.GetName().Name, AssemblyLoadContext.Default, out assembly);

            Assert.True(result);
            Assert.NotNull(assembly);
        }

        private class TestBindingProvider : ScriptBindingProvider
        {
            public TestBindingProvider(JobHostConfiguration config, JObject hostMetadata, ILogger traceWriter)
                : base(config, hostMetadata, traceWriter)
            {
            }

            public override bool TryCreate(ScriptBindingContext context, out ScriptBinding binding)
            {
                binding = null;
                return false;
            }
        }
    }
}
