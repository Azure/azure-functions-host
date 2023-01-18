// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.CodeAnalysis.Scripting;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionEntryPointResolverTests
    {
        [Fact]
        public void GetFunctionEntryPoint_WithSingleMethod_ReturnsMethod()
        {
            var resolver = new FunctionEntryPointResolver();

            var method = resolver.GetFunctionEntryPoint(new[] { new TestMethodReference("MethodName", true) });

            Assert.NotNull(method);
            Assert.Equal("MethodName", method.Name);
        }

        [Fact]
        public void GetFunctionEntryPoint_WithSinglePublicMethodAndPrivateMethods_ReturnsPublicMethod()
        {
            var resolver = new FunctionEntryPointResolver();

            var method = resolver.GetFunctionEntryPoint(new[]
            {
                new TestMethodReference("MethodName", true),
                new TestMethodReference("PrivateMethodName1", false),
                new TestMethodReference("PrivateMethodName2", false),
                new TestMethodReference("PrivateMethodName3", false),
            });

            Assert.NotNull(method);
            Assert.Equal("MethodName", method.Name);
        }

        [Fact]
        public void GetFunctionEntryPoint_WithMultiplePublicMethodsAndRunMethod_ReturnsRunMethod()
        {
            var resolver = new FunctionEntryPointResolver();

            var method = resolver.GetFunctionEntryPoint(new[]
            {
                new TestMethodReference("MethodName", true),
                new TestMethodReference("MethodName1", true),
                new TestMethodReference("Run", true),
                new TestMethodReference("PrivateMethodName3", false),
            });

            Assert.NotNull(method);
            Assert.Equal("Run", method.Name);
        }

        [Fact]
        public void GetFunctionEntryPoint_WithNoPublicMethods_ThrowsCompilationException()
        {
            var resolver = new FunctionEntryPointResolver();

            CompilationErrorException exc = Assert.Throws(typeof(CompilationErrorException), () => resolver.GetFunctionEntryPoint(new[]
            {
                new TestMethodReference("Run", false),
                new TestMethodReference("PrivateMethodName", false)
            })) as CompilationErrorException;

            var diagnostic = exc.Diagnostics.First();
            Assert.Equal(DotNetConstants.MissingFunctionEntryPointCompilationCode, diagnostic.Id);
        }

        [Fact]
        public void GetFunctionEntryPoint_WithMultiplePublicRunMethods_ThrowsCompilationException()
        {
            var resolver = new FunctionEntryPointResolver();

            CompilationErrorException exc = Assert.Throws(typeof(CompilationErrorException), () => resolver.GetFunctionEntryPoint(new[]
            {
                new TestMethodReference("Run", true),
                new TestMethodReference("Run", true),
                new TestMethodReference("Run", true)
            })) as CompilationErrorException;

            var diagnostic = exc.Diagnostics.First();
            Assert.Equal(DotNetConstants.AmbiguousFunctionEntryPointsCompilationCode, diagnostic.Id);
        }

        [Fact]
        public void GetFunctionEntryPoint_WithNamedMethod_ReturnsNamedMethod()
        {
            var resolver = new FunctionEntryPointResolver("NamedMethod");

            var method = resolver.GetFunctionEntryPoint(new[]
            {
                new TestMethodReference("NamedMethod", true),
                new TestMethodReference("Run", true),
                new TestMethodReference("Run", true),
                new TestMethodReference("PrivateMethodName", false),
            });

            Assert.NotNull(method);
            Assert.Equal("NamedMethod", method.Name);
        }

        [Fact]
        public void GetFunctionEntryPoint_WithMissingNamedMethod_ThrowsCompilationException()
        {
            var resolver = new FunctionEntryPointResolver("NamedMethod");

            CompilationErrorException exc = Assert.Throws(typeof(CompilationErrorException), () => resolver.GetFunctionEntryPoint(new[]
            {
                new TestMethodReference("Method1", true),
                new TestMethodReference("Method2", true),
                new TestMethodReference("NamedMethod", false)
            })) as CompilationErrorException;

            var diagnostic = exc.Diagnostics.First();
            Assert.Equal(DotNetConstants.InvalidEntryPointNameCompilationCode, diagnostic.Id);
        }

        private class TestMethodReference : IMethodReference
        {
            public TestMethodReference(string name, bool isPublic)
            {
                Name = name;
                IsPublic = isPublic;
            }

            public bool IsPublic { get; set; }

            public string Name { get; set; }
        }
    }
}
