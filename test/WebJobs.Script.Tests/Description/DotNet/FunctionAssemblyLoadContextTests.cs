// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
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
        [InlineData("Microsoft.Azure.WebJobs.Extensions")]
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

        // TODO: FACAVAL
       

        // TODO: FACAVAL
        //[Fact]
        //public void ResolveAssembly_WithIndirectPrivateDependency_LogsIfResolutionFails()
        //{
        //    TestLogger testLogger = new TestLogger("Test");

        //    var resolver = new FunctionAssemblyLoader("c:\\");

        //    var metadata1Directory = @"c:\testroot\test1";
        //    var metadata1 = new FunctionMetadata { Name = "Test1", ScriptFile = $@"{metadata1Directory}\test.tst" };
        //    var metadata2 = new FunctionMetadata { Name = "Test2", ScriptFile = @"c:\testroot\test2\test.tst" };

        //    var mockResolver = new Mock<IFunctionMetadataResolver>();
        //    mockResolver.Setup(m => m.ResolveAssembly("MyTestAssembly.dll"))
        //      .Returns<Assembly>(null);

        //    resolver.CreateOrUpdateContext(metadata1, this.GetType().Assembly, new FunctionMetadataResolver(metadata1.ScriptFile, new Collection<ScriptBindingProvider>(), testLogger), testLogger);
        //    resolver.CreateOrUpdateContext(metadata2, this.GetType().Assembly, mockResolver.Object, testLogger);

        //    Assembly result = resolver.ResolveAssembly(AppDomain.CurrentDomain, new System.ResolveEventArgs("MyTestAssembly.dll",
        //        new TestAssembly(new AssemblyName("MyDirectReference"), @"file:///c:/testroot/test2/bin/MyDirectReference.dll")));

        //    Assert.Null(result);
        //    Assert.Equal(1, testLogger.GetLogMessages().Count);
        //    Assert.Contains("MyTestAssembly.dll", testLogger.GetLogMessages()[0].FormattedMessage);
        //}

      
    }
}
