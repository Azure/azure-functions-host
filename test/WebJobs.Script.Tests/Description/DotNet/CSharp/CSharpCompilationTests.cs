// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class CSharpCompilationTests
    {
        [Fact]
        public void InvalidFileMetadataReference_ReturnsExpectedDiagnostics()
        {
            string code = @"
#r ""System.Runtime.dll""
public void Run(){
}";
            Script<object> script = CSharpScript.Create(code);
            var compilation = new CSharpCompilation(script.GetCompilation());

            var diagnostics = compilation.GetDiagnostics();

            var diagnostic = diagnostics.FirstOrDefault(d => string.Compare(d.Id, DotNetConstants.InvalidFileMetadataReferenceCode) == 0);

            Assert.NotNull(diagnostic);
            Assert.Equal("The reference 'System.Runtime.dll' is invalid. If you are attempting to add a framework reference, please remove the '.dll' file extension.",
                diagnostic.GetMessage());
        }

        [Fact]
        public void Compilation_WithErrors_HasExpectedDiagnostics()
        {
            string code = @"
public void Run(){
  invalid.SomeMethod();
}";
            Script<object> script = CSharpScript.Create(code);
            var compilation = new CSharpCompilation(script.GetCompilation());

            ImmutableArray<Diagnostic> diagnostics = compilation.GetDiagnostics();

            Assert.Equal(1, diagnostics.Count());
        }

        [Fact]
        public void AsyncVoid_ReturnsExpectedDiagnostics()
        {
            string code = @"
public async void Run(){
await System.Threading.Tasks.Task.Run(() => {});
}";
            Script<object> script = CSharpScript.Create(code);
            var compilation = new CSharpCompilation(script.GetCompilation());

            var diagnostic = compilation.GetDiagnostics().First();

            Assert.Equal("This method has the async keyword but it returns void",
                diagnostic.GetMessage());
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        }
    }
}
