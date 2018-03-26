// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Description.DotNet
{
    public class DynamicFunctionAssemblyLoadContextTests
    {
        [Fact]
        public void SharedAssembly_IsLoadedIntoSharedContext()
        {
            using (var tempFolder = new TempDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())))
            using (var env = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsScriptRoot, tempFolder.Path))
            {
                // Arrange
                var sharedContext = new FunctionAssemblyLoadContext(tempFolder.Path);
                Assembly targetAssembly = typeof(DynamicFunctionAssemblyLoadContextTests).Assembly;
                string originalLocation = targetAssembly.Location;
                string targetAssemblyLocation = Path.Combine(tempFolder.Path, Path.GetFileName(originalLocation));

                File.Copy(originalLocation, targetAssemblyLocation);

                var metadata1Directory = @"c:\testroot\test1";
                var metadata1 = new WebJobs.Script.Description.FunctionMetadata { Name = "Test1", ScriptFile = $@"{metadata1Directory}\test.tst" };

                var mockResolver = new Mock<IFunctionMetadataResolver>();

                var loadContext = new TestDynamicAssemblyLoadContext(metadata1, mockResolver.Object, NullLogger.Instance, sharedContext);

                // Act
                Assembly result = loadContext.LoadFromAssemblyName(targetAssembly.GetName());

                // Assert
                Assert.NotNull(result);
                Assert.Same(sharedContext, AssemblyLoadContext.GetLoadContext(result));
            }
        }

        private class TestDynamicAssemblyLoadContext : DynamicFunctionAssemblyLoadContext
        {
            private readonly FunctionAssemblyLoadContext _sharedContext;

            public TestDynamicAssemblyLoadContext(WebJobs.Script.Description.FunctionMetadata functionMetadata, 
                IFunctionMetadataResolver resolver, ILogger logger, FunctionAssemblyLoadContext sharedContext)
                : base(functionMetadata, resolver, logger)
            {
                _sharedContext = sharedContext;
            }

            protected override FunctionAssemblyLoadContext SharedContext => _sharedContext;
        }
    }
}
