// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Description;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Description.DotNet
{
    public class RawAssemblyCompilationTests
    {
        [Theory]
        [MemberData(nameof(GetTargetNames))]
        public void GetEntryPointSignature_BindsToExpectedMethod(string entryPointName, string methodName)
        {
            var testAssembly = typeof(TestFunction1).Assembly;
            string assemblyPath = new Uri(testAssembly.Location, UriKind.Absolute).LocalPath;
            var compilation = new RawAssemblyCompilation(assemblyPath, entryPointName);

            FunctionSignature signature = compilation.GetEntryPointSignature(new FunctionEntryPointResolver(), testAssembly);

            Assert.NotNull(signature);
            Assert.Equal(methodName, signature.MethodName);
        }

        [Fact]
        public void GetEntryPointSignature_PrefersStaticMethod()
        {
            var testAssembly = typeof(TestFunction1).Assembly;
            string assemblyPath = new Uri(testAssembly.Location, UriKind.Absolute).LocalPath;
            var compilation = new RawAssemblyCompilation(assemblyPath, $"{typeof(TestFunction3).FullName}.{nameof(TestFunction3.Run)}");

            FunctionSignature signature = compilation.GetEntryPointSignature(new FunctionEntryPointResolver(), testAssembly);

            Assert.NotNull(signature);
            Assert.Equal(nameof(TestFunction3.Run), signature.MethodName);
        }

        public static IEnumerable<object[]> GetTargetNames()
        {
            return new[]
            {
                new[] { $"{typeof(TestFunction1).FullName}.{nameof(TestFunction1.Run)}", nameof(TestFunction1.Run) },
                new[] { $"{typeof(TestFunction2).FullName}.{nameof(TestFunction2.Run)}", nameof(TestFunction2.Run) }
            };
        }
    }

    public class TestFunction1
    {
        public void Run() { }
    }

    public class TestFunction2
    {
        public static void Run() { }
    }

    public class TestFunction3
    {
        public void Run() { }

        public static void Run(string test) { }
    }
}