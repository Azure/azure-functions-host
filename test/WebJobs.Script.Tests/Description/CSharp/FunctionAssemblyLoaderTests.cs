// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.CodeAnalysis;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Description.CSharp
{
    public class FunctionAssemblyLoaderTests
    {
        [Fact]
        public void ResolveAssembly_WithIndirectPrivateDependency_IsResolved()
        {
            var resolver = new FunctionAssemblyLoader("c:\\");

            var metadata1 = new FunctionMetadata { Name = "Test1", ScriptFile = @"c:\testroot\test1\test.tst" };
            var metadata2 = new FunctionMetadata { Name = "Test2", ScriptFile = @"c:\testroot\test2\test.tst" };
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);

            var mockResolver = new Mock<IFunctionMetadataResolver>();
            mockResolver.Setup(m => m.ResolveAssembly("MyTestAssembly.dll"))
              .Returns(new TestAssembly(new AssemblyName("MyTestAssembly")));
                
            resolver.CreateOrUpdateContext(metadata1, this.GetType().Assembly, new FunctionMetadataResolver(metadata1, new Collection<ScriptBindingProvider>(), traceWriter), traceWriter);
            resolver.CreateOrUpdateContext(metadata2, this.GetType().Assembly, mockResolver.Object, traceWriter);

            Assembly result = resolver.ResolveAssembly(null, new System.ResolveEventArgs("MyTestAssembly.dll",
                new TestAssembly(new AssemblyName("MyDirectReference"), @"file:///c:/testroot/test2/bin/MyDirectReference.dll")));

            Assert.NotNull(result);
        }

        [Fact]
        public void ResolveAssembly_WithIndirectPrivateDependency_LogsIfResolutionFails()
        {
            var resolver = new FunctionAssemblyLoader("c:\\");

            var metadata1 = new FunctionMetadata { Name = "Test1", ScriptFile = @"c:\testroot\test1\test.tst" };
            var metadata2 = new FunctionMetadata { Name = "Test2", ScriptFile = @"c:\testroot\test2\test.tst" };
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);

            var mockResolver = new Mock<IFunctionMetadataResolver>();
            mockResolver.Setup(m => m.ResolveAssembly("MyTestAssembly.dll"))
              .Returns<Assembly>(null);

            resolver.CreateOrUpdateContext(metadata1, this.GetType().Assembly, new FunctionMetadataResolver(metadata1, new Collection<ScriptBindingProvider>(), traceWriter), traceWriter);
            resolver.CreateOrUpdateContext(metadata2, this.GetType().Assembly, mockResolver.Object, traceWriter);

            Assembly result = resolver.ResolveAssembly(null, new System.ResolveEventArgs("MyTestAssembly.dll",
                new TestAssembly(new AssemblyName("MyDirectReference"), @"file:///c:/testroot/test2/bin/MyDirectReference.dll")));

            Assert.Null(result);
            Assert.Equal(1, traceWriter.Traces.Count);
            Assert.Contains("MyTestAssembly.dll", traceWriter.Traces[0].Message);
        }

        private class TestAssembly : Assembly
        {
            private readonly AssemblyName _name;
            private readonly string _codebase;

            public TestAssembly(AssemblyName name, string codebase = null)
            {
                _name = name;
                _codebase = codebase;
            }

            public override string CodeBase
            {
                get
                {
                    return _codebase;
                }
            }

            public override string FullName
            {
                get
                {
                    return _name.FullName;
                }
            }

            public override AssemblyName GetName()
            {
                return _name;
            }
        }
    }
}
