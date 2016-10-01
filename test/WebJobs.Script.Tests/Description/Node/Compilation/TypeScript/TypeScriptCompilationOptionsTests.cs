// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Description.Node.TypeScript;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Description.Node.Compilation.TypeScript
{
    public class TypeScriptCompilationOptionsTests
    {
        [Fact]
        public void ToArgumentString_ReturnsExpectedValue()
        {
            var options = new TypeScriptCompilationOptions()
            {
                OutDir = ".output",
                ToolPath = "tsc.exe"
            };

            string input = @"c:\root\test\index.ts";

            string result = options.ToArgumentString(input);

            Assert.Equal($"{input} --target ES2015 --module commonjs --outdir .output", result);
        }
    }
}
