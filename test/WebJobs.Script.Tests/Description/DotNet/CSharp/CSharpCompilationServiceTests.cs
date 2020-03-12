// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Description;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Description.DotNet.CSharp
{
    public class CSharpCompilationServiceTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GetFunctionSource_PreservesByteOrderMark(bool emitBom)
        {
            var metadataResolverMock = new Mock<IFunctionMetadataResolver>();
            var service = new CSharpCompilationService(metadataResolverMock.Object, CodeAnalysis.OptimizationLevel.Debug);
            var function1 = @"using System;
public static void Run(string id, out string output)
{
    output = string.Empty;
}";

            using (var directory = new TempDirectory())
            {
                string path = Path.Combine(directory.Path, "run.csx");
                using (var writer = new StreamWriter(path, false, new UTF8Encoding(emitBom), 4096))
                {
                    writer.Write(function1);
                }

                var testMedatada = new FunctionMetadata { Name = "Test1", ScriptFile = path };
                string code = CSharpCompilationService.GetFunctionSource(testMedatada);

                bool hasBom = Utility.HasUtf8ByteOrderMark(code);

                Assert.Equal(emitBom, hasBom);
            }
        }
    }
}
