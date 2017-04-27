// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Description.Node.Compilation.Raw
{
    public class RawJavascriptCompilationTests
    {
        [Fact]
        public void Emit_ReturnsScriptPath()
        {
            string path = @"c:\root\test\index.js";
            var compilation = new RawJavaScriptCompilation(path);

            string result = compilation.Emit(CancellationToken.None);

            Assert.Equal(path, result);
        }
    }
}
