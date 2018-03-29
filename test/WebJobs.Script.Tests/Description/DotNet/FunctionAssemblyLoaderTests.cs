// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Internal;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionAssemblyLoaderTests : IDisposable
    {
        private readonly FunctionAssemblyLoader resolver;

        public FunctionAssemblyLoaderTests()
        {
            resolver = new FunctionAssemblyLoader("c:\\");
        }

        public void Dispose() => resolver?.Dispose();

        [Fact]
        public void ResolveAssembly_WithIndirectPrivateDependency_IsResolved()
        {
            var metadata1Directory = @"c:\testroot\test1";
            var metadata1 = new FunctionMetadata { Name = "Test1", ScriptFile = $@"{metadata1Directory}\test.tst" };
            var metadata2 = new FunctionMetadata { Name = "Test2", ScriptFile = @"c:\testroot\test2\test.tst" };
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);

            var mockResolver = new Mock<IFunctionMetadataResolver>();
            mockResolver.Setup(m => m.ResolveAssembly("MyTestAssembly.dll"))
              .Returns(new TestAssembly(new AssemblyName("MyTestAssembly")));

            resolver.CreateOrUpdateContext(metadata1, this.GetType().Assembly, new FunctionMetadataResolver(metadata1.ScriptFile, new Collection<ScriptBindingProvider>(), traceWriter, null), traceWriter, null);
            resolver.CreateOrUpdateContext(metadata2, this.GetType().Assembly, mockResolver.Object, traceWriter, null);

            Assembly result = resolver.ResolveAssembly(null, new System.ResolveEventArgs("MyTestAssembly.dll",
                new TestAssembly(new AssemblyName("MyDirectReference"), @"file:///c:/testroot/test2/bin/MyDirectReference.dll")));

            Assert.NotNull(result);
        }

        [Fact]
        public void ResolveAssembly_WithIndirectPrivateDependency_LogsIfResolutionFails()
        {
            var metadata1Directory = @"c:\testroot\test1";
            var metadata1 = new FunctionMetadata { Name = "Test1", ScriptFile = $@"{metadata1Directory}\test.tst" };
            var metadata2 = new FunctionMetadata { Name = "Test2", ScriptFile = @"c:\testroot\test2\test.tst" };
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);

            var mockResolver = new Mock<IFunctionMetadataResolver>();
            mockResolver.Setup(m => m.ResolveAssembly("MyTestAssembly.dll"))
              .Returns<Assembly>(null);

            resolver.CreateOrUpdateContext(metadata1, this.GetType().Assembly, new FunctionMetadataResolver(metadata1.ScriptFile, new Collection<ScriptBindingProvider>(), traceWriter, null), traceWriter, null);
            resolver.CreateOrUpdateContext(metadata2, this.GetType().Assembly, mockResolver.Object, traceWriter, null);

            Assembly result = resolver.ResolveAssembly(AppDomain.CurrentDomain, new System.ResolveEventArgs("MyTestAssembly.dll",
                new TestAssembly(new AssemblyName("MyDirectReference"), @"file:///c:/testroot/test2/bin/MyDirectReference.dll")));

            var traces = traceWriter.GetTraces();
            Assert.Null(result);
            Assert.Equal(1, traces.Count);
            Assert.Contains("MyTestAssembly.dll", traces.First().Message);
        }

        [Fact]
        public void ResolveAssembly_SwallowsNonFatalException()
        {
            // Set up a context to be resolved correctly
            var metadata1Directory = @"c:\testroot\test1";
            var metadata1 = new FunctionMetadata { Name = "Test1", ScriptFile = $@"{metadata1Directory}\test.tst" };
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);

            // Set up for an error to occur when resolving the assembly
            var mockResolver = new Mock<IFunctionMetadataResolver>();
            mockResolver.Setup(m => m.ResolveAssembly("MyTestAssembly.dll"))
              .Throws(new System.IO.FileNotFoundException());

            // Set up for an error to occur in the error handling path!
            var mockLogger = new Mock<ILogger>();
            mockLogger.Setup(m => m.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<FormattedLogValues>(), It.IsAny<Exception>(), It.IsAny<Func<object, Exception, string>>()))
                .Throws(new NullReferenceException());

            var mockLoggerFactory = new Mock<ILoggerFactory>();
            mockLoggerFactory.Setup(m => m.CreateLogger(LogCategories.Startup))
                .Returns(mockLogger.Object);

            resolver.CreateOrUpdateContext(metadata1, this.GetType().Assembly, mockResolver.Object, traceWriter, mockLoggerFactory.Object);

            Assembly result = resolver.ResolveAssembly(AppDomain.CurrentDomain, new System.ResolveEventArgs("MyTestAssembly.dll",
                new TestAssembly(new AssemblyName("MyDirectReference"), @"file:///c:/testroot/test1/bin/MyDirectReference.dll")));

            // The error in the error handling path should have been swallowed and we just get a null back
            Assert.Null(result);
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
