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
            string depsPath = Path.Combine(Directory.GetCurrentDirectory(), "Description", "DotNet", "TestFiles", "DepsFiles");

            (IDictionary<string, string> depsAssemblies, IDictionary<string, string> nativeLibraries) =
                FunctionAssemblyLoadContext.InitializeDeps(depsPath);

            string testRid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" : "unix";

            // Ensure runtime specific dependencies are resolved, with appropriate RID
            Assert.Contains($"runtimes/{testRid}/lib/netstandard2.0/System.Private.ServiceModel.dll", depsAssemblies.Values);
            Assert.Contains($"runtimes/{testRid}/lib/netstandard1.3/System.Text.Encoding.CodePages.dll", depsAssemblies.Values);

            // Ensure flattened dependency has expected path
            Assert.Contains($"Microsoft.Azure.WebJobs.Host.Storage.dll", depsAssemblies.Values);

            // Ensure native libraries are resolved, with appropriate RID and path
            string nativeRid;
            string nativePrefix;
            string nativeExtension;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                nativeRid = "win-";
                nativePrefix = string.Empty;
                nativeExtension = "dll";
            }
            else
            {
                nativePrefix = "lib";
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    nativeRid = "osx-";
                    nativeExtension = "dylib";
                }
                else
                {
                    nativeRid = "linux-";
                    nativeExtension = "so";
                }
            }

            nativeRid += Environment.Is64BitProcess ? "x64" : "x86";

            Assert.Contains($"runtimes/{nativeRid}/nativeassets/netstandard2.0/{nativePrefix}CpuMathNative.{nativeExtension}", nativeLibraries.Values);
        }
    }
}
