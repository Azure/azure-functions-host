// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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

        [Fact]
        public void InvalidPrivateReference_ReturnsExpectedDiagnostics()
        {
            string code = @"
#r ""PrivateReference""
public async void Run(){
await System.Threading.Tasks.Task.Run(() => {});
}";
            var file = Path.GetTempFileName();
            file = Path.ChangeExtension(file, "dll");

            Script<object> script = CSharpScript.Create(code)
                .WithOptions(ScriptOptions.Default.WithMetadataResolver(new TestFunctionMetadataResolver(file)));

            var compilation = new CSharpCompilation(script.GetCompilation());

            var diagnostic = compilation
                .GetDiagnostics()
                .First(d => string.Equals(d.Id, DotNetConstants.InvalidPrivateMetadataReferenceCode, System.StringComparison.Ordinal));

            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Contains(Path.GetFileName(file), diagnostic.GetMessage());
        }

        private class TestFunctionMetadataResolver : MetadataReferenceResolver, IFunctionMetadataResolver
        {
            private readonly string _privateAssemblyFilePath;

            public TestFunctionMetadataResolver(string privateAssemblyFilePath)
            {
                _privateAssemblyFilePath = privateAssemblyFilePath;
            }

            public ScriptOptions CreateScriptOptions()
            {
                throw new System.NotImplementedException();
            }

            public override bool Equals(object other)
            {
                throw new System.NotImplementedException();
            }

            public IReadOnlyCollection<string> GetCompilationReferences()
            {
                throw new System.NotImplementedException();
            }

            public override int GetHashCode()
            {
                throw new System.NotImplementedException();
            }

            public bool RequiresPackageRestore(FunctionMetadata metadata)
            {
                throw new System.NotImplementedException();
            }

            public Assembly ResolveAssembly(string assemblyName)
            {
                throw new System.NotImplementedException();
            }

            public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties)
            {
                return ImmutableArray<PortableExecutableReference>.Empty;
            }

            public Task<PackageRestoreResult> RestorePackagesAsync()
            {
                throw new System.NotImplementedException();
            }

            public bool TryGetPackageReference(string referenceName, out PackageReference package)
            {
                throw new System.NotImplementedException();
            }

            public bool TryResolvePrivateAssembly(string name, out string assemblyPath)
            {
                assemblyPath = _privateAssemblyFilePath;
                return true;
            }
        }
    }
}
