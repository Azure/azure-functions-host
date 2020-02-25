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
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.DependencyModel;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionAssemblyLoadContextTests
    {
        [Theory]
        [InlineData("Microsoft.Azure.WebJobs, Version=3.0.0.0")]
        [InlineData("Microsoft.Azure.WebJobs.Extensions.Http, Version=3.0.0.0")]
        [InlineData("Microsoft.Azure.WebJobs.Host, Version=3.0.0.0")]
        [InlineData("Microsoft.Azure.WebJobs.Logging, Version=3.0.0.0")]
        [InlineData("Microsoft.Azure.WebJobs.Logging.ApplicationInsights, Version=3.0.0.0")]
        [InlineData("Microsoft.Azure.WebJobs.Script, Version=3.0.0.0")]
        [InlineData("Microsoft.Azure.WebJobs.Script.Grpc, Version=3.0.0.0")]
        [InlineData("Microsoft.Azure.WebJobs.Script.WebHost, Version=3.0.0.0")]
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
            List<string> currentRidFallbacks = DependencyHelper.GetRuntimeFallbacks();

            (IDictionary<string, RuntimeAsset[]> depsAssemblies, IDictionary<string, RuntimeAsset[]> nativeLibraries) =
                FunctionAssemblyLoadContext.InitializeDeps(depsPath, currentRidFallbacks);

            string testRid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" : "unix";

            // Ensure runtime specific dependencies are resolved, with appropriate RID
            FunctionAssemblyLoadContext.TryGetDepsAsset(depsAssemblies, "System.private.servicemodel", currentRidFallbacks, out string assemblyPath);
            Assert.Equal($"runtimes/{testRid}/lib/netstandard2.0/System.Private.ServiceModel.dll", assemblyPath);

            FunctionAssemblyLoadContext.TryGetDepsAsset(depsAssemblies, "System.text.encoding.codepages", currentRidFallbacks, out assemblyPath);
            Assert.Equal($"runtimes/{testRid}/lib/netstandard1.3/System.Text.Encoding.CodePages.dll", assemblyPath);

            // Ensure flattened dependency has expected path
            FunctionAssemblyLoadContext.TryGetDepsAsset(depsAssemblies, "Microsoft.Azure.WebJobs.Host.Storage", currentRidFallbacks, out assemblyPath);
            Assert.Equal($"Microsoft.Azure.WebJobs.Host.Storage.dll", assemblyPath);

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

            string nativeAssetFileName = $"{nativePrefix}CpuMathNative.{nativeExtension}";

            FunctionAssemblyLoadContext.TryGetDepsAsset(nativeLibraries, nativeAssetFileName, currentRidFallbacks, out string assetPath);
            Assert.Contains($"runtimes/{nativeRid}/nativeassets/netstandard2.0/{nativeAssetFileName}", assetPath);
        }

        [Theory]
        [InlineData("win10-x64", "win7-x64", "", "dll", true)]
        [InlineData("win7-x64", "win7-x64", "", "dll", true)]
        [InlineData("win8-x64", "win7-x64", "", "dll", true)]
        [InlineData("win10", "", "", "dll", false)]
        [InlineData("win-x64", "win-x64", "", "dll", true)]
        public void InitializeDeps_WithRidSpecificNativeAssets_LoadsExpectedDependencies(string rid, string expectedNativeRid, string prefix, string suffix, bool expectMatch)
        {
            string depsPath = Path.Combine(Directory.GetCurrentDirectory(), "Description", "DotNet", "TestFiles", "DepsFiles", "RidNativeDeps");

            List<string> ridFallback = DependencyHelper.GetRuntimeFallbacks(rid);

            (_, IDictionary<string, RuntimeAsset[]> nativeLibraries) =
                FunctionAssemblyLoadContext.InitializeDeps(depsPath, ridFallback);

            string nativeAssetFileName = $"{prefix}Cosmos.CRTCompat.{suffix}";

            FunctionAssemblyLoadContext.TryGetDepsAsset(nativeLibraries, nativeAssetFileName, ridFallback, out string assetPath);

            string expectedMatch = expectMatch
                ? $"runtimes/{expectedNativeRid}/native/{nativeAssetFileName}"
                : null;

            Assert.Equal(expectedMatch, assetPath);
        }
    }
}
