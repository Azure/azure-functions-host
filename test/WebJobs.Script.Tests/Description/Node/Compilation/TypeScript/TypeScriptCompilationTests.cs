// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Description.Node.TypeScript
{
    public class TypeScriptCompilationTests
    {
        [Fact]
        public async Task Emit_ReturnsExpectedPath()
        {
            var options = new TypeScriptCompilationOptions
            {
                OutDir = "outdir",
                RootDir = @"c:\root\directory",
                Target = "test.ts"
            };

            string inputFile = @"c:\root\directory\functionname\inputfile.ts";
            var compilation = await TypeScriptCompilation.CompileAsync(inputFile, options, new TestCompiler());

            string result = compilation.Emit(CancellationToken.None);

            Assert.Equal(@"c:\root\directory\functionname\outdir\functionname\inputfile.js", result);
        }

        private class TestCompiler : ITypeScriptCompiler
        {
            public Task<ImmutableArray<Diagnostic>> CompileAsync(string inputFile, TypeScriptCompilationOptions options)
            {
                return Task.FromResult(ImmutableArray<Diagnostic>.Empty);
            }
        }
    }
}
