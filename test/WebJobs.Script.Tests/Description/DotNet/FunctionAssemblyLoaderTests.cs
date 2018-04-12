﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionAssemblyLoaderTests
    {
        // TODO: FACAVAL
        //[Fact]
        //public void ResolveAssembly_WithIndirectPrivateDependency_IsResolved()
        //{
        //    TestLogger testLogger = new TestLogger("Test");

        //    var resolver = new FunctionAssemblyLoader("c:\\");

        //    var metadata1Directory = @"c:\testroot\test1";
        //    var metadata1 = new FunctionMetadata { Name = "Test1", ScriptFile = $@"{metadata1Directory}\test.tst" };
        //    var metadata2 = new FunctionMetadata { Name = "Test2", ScriptFile = @"c:\testroot\test2\test.tst" };

        //    var mockResolver = new Mock<IFunctionMetadataResolver>();
        //    mockResolver.Setup(m => m.ResolveAssembly("MyTestAssembly.dll"))
        //      .Returns(new TestAssembly(new AssemblyName("MyTestAssembly")));

        //    resolver.CreateOrUpdateContext(metadata1, this.GetType().Assembly, new FunctionMetadataResolver(metadata1.ScriptFile, new Collection<ScriptBindingProvider>(), testLogger), testLogger);
        //    resolver.CreateOrUpdateContext(metadata2, this.GetType().Assembly, mockResolver.Object, testLogger);

        //    Assembly result = resolver.ResolveAssembly(null, new System.ResolveEventArgs("MyTestAssembly.dll",
        //        new TestAssembly(new AssemblyName("MyDirectReference"), @"file:///c:/testroot/test2/bin/MyDirectReference.dll")));

        //    Assert.NotNull(result);
        //}

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
