// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionAssemblyLoadContextTests
    {
        [Theory]
        [InlineData("Microsoft.Azure.WebJobs")]
        [InlineData("Microsoft.Azure.WebJobs.Extensions.Http")]
        [InlineData("Microsoft.Azure.WebJobs.Host")]
        [InlineData("Microsoft.Azure.WebJobs.Logging")]
        [InlineData("Microsoft.Azure.WebJobs.Logging.ApplicationInsights")]
        [InlineData("Microsoft.Azure.WebJobs.Script")]
        [InlineData("Microsoft.Azure.WebJobs.Script.Grpc")]
        [InlineData("Microsoft.Azure.WebJobs.Script.WebHost")]
        [InlineData("Microsoft.Azure.WebSites.DataProtection")]
        [InlineData("System.IO")] // System.*
        public void RuntimeAssemblies_AreLoadedInDefaultContext(string assemblyName)
        {
            var functionContext = new FunctionAssemblyLoadContext(AppContext.BaseDirectory);

            var assembly = functionContext.LoadFromAssemblyName(new AssemblyName(assemblyName));

            Assert.NotNull(assembly);
            Assert.NotSame(functionContext, AssemblyLoadContext.GetLoadContext(assembly));
            Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(assembly));
        }

        [Fact]
        public void InitializeDeps_LoadsExpectedDependencies()
        {
            string depsPath = Path.Combine(Directory.GetCurrentDirectory(), @"Description\DotNet\TestFiles\DepsFiles");

            IDictionary<string, string> assemblies = FunctionAssemblyLoadContext.InitializeDeps(depsPath);

            string testRid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" : "unix";

            // Ensure runtime specific dependencies are resolved, with appropriate RID
            Assert.Contains($"runtimes/{testRid}/lib/netstandard2.0/System.Private.ServiceModel.dll", assemblies.Values);
            Assert.Contains($"runtimes/{testRid}/lib/netstandard1.3/System.Text.Encoding.CodePages.dll", assemblies.Values);

            // Ensure flattened dependency has expected path
            Assert.Contains($"Microsoft.Azure.WebJobs.Host.Storage.dll", assemblies.Values);
        }
    }
}
