// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    public class JobHostTests
    {
        // Test that we can do static initialization before runtime initialization. 
        [Fact]
        public async Task StaticInit()
        {
            IStorageAccount account = new FakeStorageAccount();
            var config = TestHelpers.NewConfig(account);
            
            // Can do the static init. Get the binders. 
            var ctx = config.CreateStaticServices();
            var provider = ctx.GetService<IBindingProvider>();

            var attr = new BlobAttribute("container/path", FileAccess.Read);
            var result1 = await ScriptHelpers.CanBindAsync(provider, attr, typeof(TextReader));
            var result2 = await ScriptHelpers.CanBindAsync(provider, attr, typeof(TextWriter));

            Assert.True(result1);
            Assert.False(result2);

            // Can now set type locator and types, do indexing, and run. 
            // Important that we're able to set this *after* we've queried the binding graph. 
            config.TypeLocator = new FakeTypeLocator(typeof(ProgramSimple));

            var expected = "123";
            using (var jobHost = new JobHost(config))
            {
                var method = typeof(ProgramSimple).GetMethod("Test");
                jobHost.Call(method, new { value = expected });
                Assert.Equal(expected, ProgramSimple._value);
            }
        }

        private class ProgramSimple
        {
            public static string _value; // evidence of execution

            [NoAutomaticTrigger]
            public static void Test(string value)
            {
                _value = value;
            }
        }
    }
}
