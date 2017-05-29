// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description.Node.TypeScript;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Description.Node.Compilation.TypeScript
{
    public class TypeScriptCompilerTests
    {
        [Fact]
        public void TryParseDiagnostics_ReturnsExpectedResult()
        {
            string filename = "index.ts";
            int line = 8;
            int column = 5;
            string level = "error";
            string code = "TS2304";
            string message = "Cannot find name 'something'";
            string input = $"{filename}({line},{column}): {level} {code}: {message}";
            bool parsed = TypeScriptCompiler.TryParseDiagnostic(input, out Diagnostic diagnostic);

            Assert.True(parsed);
            Assert.Equal(code, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(filename, diagnostic.Location.GetLineSpan().Path);
            Assert.Equal(input, diagnostic.ToString());
        }

        [Fact]
        public void TryParseDiagnostics_WithInvalidData_ReturnsExpectedResult()
        {
            string input = $"abcde(8,4) : test : test";
            bool parsed = TypeScriptCompiler.TryParseDiagnostic(input, out Diagnostic diagnostic);

            Assert.False(parsed);
        }
    }
}
