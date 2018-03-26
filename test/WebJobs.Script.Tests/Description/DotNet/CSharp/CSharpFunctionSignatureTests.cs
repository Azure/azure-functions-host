// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class CSharpFunctionSignatureTests
    {
        [Fact]
        public void Matches_IsTrue_WhenParametersAreEqual()
        {
            var function1 = @"using System;
public static void Run(string id, out string output)
{
    output = string.Empty;
}";

            var function2 = @"using System;
public static void Run(string id, out string output)
{
    string result = string.Empty;
    output = result;
}";

            Tuple<FunctionSignature, FunctionSignature> signatures = GetFunctionSignatures(function1, function2);

            Assert.True(signatures.Item1.Equals(signatures.Item2));
            Assert.Equal(signatures.Item1.GetHashCode(), signatures.Item2.GetHashCode());
        }

        [Fact]
        public void Matches_IsFalse_WhenReturnTypesAreDifferent()
        {
            var function1 = @"using System;
public static string Run(string id, out string output)
{
    output = string.Empty;
    return string.Empty;
}";

            var function2 = @"using System;
public static void Run(string id, out string output)
{
    string result = string.Empty;
    output = result;
}";

            Tuple<FunctionSignature, FunctionSignature> signatures = GetFunctionSignatures(function1, function2);

            Assert.False(signatures.Item1.Equals(signatures.Item2));
            Assert.NotEqual(signatures.Item1.GetHashCode(), signatures.Item2.GetHashCode());
        }

        [Fact]
        public void Matches_IsFalse_WhenParametersAreNotEqual()
        {
            var function1 = @"using System;
public static void Run(string identity, out string outputParam)
{
    outputParam = string.Empty;
}";

            var function2 = @"using System;
public static void Run(string id, out string output)
{
    string result = string.Empty;
    output = result;
}";

            Tuple<FunctionSignature, FunctionSignature> signatures = GetFunctionSignatures(function1, function2);

            Assert.False(signatures.Item1.Equals(signatures.Item2));
            Assert.NotEqual(signatures.Item1.GetHashCode(), signatures.Item2.GetHashCode());
        }

        [Fact]
        public void GetMethod_ReturnsExpectedMethod()
        {
            var function1 = @"using System;
namespace Test.Function1
{
    public class Function
    {
        public static void Run(string identity, out string outputParam)
        {
            outputParam = nameof(Function1);
        }
    }
}

namespace Test.Function2
{
    public class Function
    {
        public static void Run(string identity, out string outputParam)
        {
            outputParam = nameof(Function2);
        }
    }
}
";

            using (var path = new TempDirectory())
            {
                var tree = CSharpSyntaxTree.ParseText(function1);
                var references = new MetadataReference[] { MetadataReference.CreateFromFile(typeof(string).Assembly.Location) };

                var compilation = CodeAnalysis.CSharp.CSharpCompilation.Create("TestAssembly", new[] { tree }, references: references)
                    .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

                string assemblyPath = Path.Combine(path.Path, "TestAssembly.dll");
                compilation.Emit(assemblyPath);

                Assembly assembly = Assembly.LoadFrom(assemblyPath);

                var parameters = new List<FunctionParameter>
                {
                    new FunctionParameter("identity", typeof(string).FullName, false, RefKind.None),
                    new FunctionParameter("outputParam", typeof(string).FullName, false, RefKind.Out)
                };

                var signature1 = new FunctionSignature("Test.Function1.Function", "Run", parameters.ToImmutableArray(), typeof(void).FullName, false);
                var signature2 = new FunctionSignature("Test.Function2.Function", "Run", parameters.ToImmutableArray(), typeof(void).FullName, false);

                var method1 = signature1.GetMethod(assembly);
                var method2 = signature2.GetMethod(assembly);

                Assert.Equal("Test.Function1.Function", method1.DeclaringType.FullName);
                Assert.Equal("Test.Function2.Function", method2.DeclaringType.FullName);
            }
        }

        private Tuple<FunctionSignature, FunctionSignature> GetFunctionSignatures(string function1, string function2)
        {
            var tree1 = CSharpSyntaxTree.ParseText(function1, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
            var tree2 = CSharpSyntaxTree.ParseText(function2, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));

            var references = new MetadataReference[] { MetadataReference.CreateFromFile(typeof(string).Assembly.Location) };

            var compilation1 = new Script.Description.CSharpCompilation(CodeAnalysis.CSharp.CSharpCompilation.Create("test1", references: references)
                   .AddSyntaxTrees(tree1));

            var compilation2 = new Script.Description.CSharpCompilation(CodeAnalysis.CSharp.CSharpCompilation.Create("test2", references: references)
                .AddSyntaxTrees(tree2));

            var signature1 = compilation1.GetEntryPointSignature(new FunctionEntryPointResolver(), null);
            var signature2 = compilation2.GetEntryPointSignature(new FunctionEntryPointResolver(), null);

            return Tuple.Create(signature1, signature2);
        }

        [Fact]
        public void Matches_IsTrue_WhenParametersAreEquivalent()
        {
            var function1 = @"using System;
public static void Run(string id, out string output)
{
    output = string.Empty;
}";

            // Diferent formatting, qualified name, not using alias
            var function2 = @"using System;
public static void Run( System.String id , 
out String output )
{
    string result = string.Empty;
    output = result;
}";

            Tuple<FunctionSignature, FunctionSignature> signatures = GetFunctionSignatures(function1, function2);

            Assert.True(signatures.Item1.Equals(signatures.Item2));
            Assert.Equal(signatures.Item1.GetHashCode(), signatures.Item2.GetHashCode());
        }

        [Fact]
        public void Matches_IsTrue_WhenUsingLocalTypes()
        {
            var function1 = @"using System;
public static void Run(Test id, out string output)
{
    output = string.Empty;
}

public class Test 
{
    public string Id { get; set; }
}";

            var tree = CSharpSyntaxTree.ParseText(function1, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
            var references = new MetadataReference[] { MetadataReference.CreateFromFile(typeof(string).Assembly.Location) };
            var compilation = CodeAnalysis.CSharp.CSharpCompilation.Create("test1", references: references).AddSyntaxTrees(tree);

            var signature1 = new Script.Description.CSharpCompilation(compilation).GetEntryPointSignature(new FunctionEntryPointResolver(), null);

            Assert.True(signature1.HasLocalTypeReference);
        }

        [Fact]
        public void Matches_IsTrue_WhenUsingLocalTypesAsGenericArguments()
        {
            var function1 = @"using System;
using System.Collections.Generic;
public static void Run(string id, ICollection<Test> test1)
{
    
}

public class Test 
{
    public string Id { get; set; }
}";

            var tree = CSharpSyntaxTree.ParseText(function1, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
            var references = new MetadataReference[] { MetadataReference.CreateFromFile(typeof(string).Assembly.Location) };
            var compilation = CodeAnalysis.CSharp.CSharpCompilation.Create("test1", references: references).AddSyntaxTrees(tree);

            var signature1 = new Script.Description.CSharpCompilation(compilation).GetEntryPointSignature(new FunctionEntryPointResolver(), null);

            Assert.True(signature1.HasLocalTypeReference);
        }

        [Fact]
        public void Matches_IsTrue_WhenUsingLocalTypesInReturn()
        {
            var function1 = @"using System;
using System.Collections.Generic;
public static Test Run(string id)
{
    return null;
}

public class Test 
{
    public string Id { get; set; }
}";

            var tree = CSharpSyntaxTree.ParseText(function1, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
            var references = new MetadataReference[] { MetadataReference.CreateFromFile(typeof(string).Assembly.Location) };
            var compilation = CodeAnalysis.CSharp.CSharpCompilation.Create("test1", references: references).AddSyntaxTrees(tree);

            var signature1 = new Script.Description.CSharpCompilation(compilation).GetEntryPointSignature(new FunctionEntryPointResolver(), null);

            Assert.True(signature1.HasLocalTypeReference);
        }

        [Fact]
        public void Matches_IsTrue_WhenUsingLocalTypesInGenericReturn()
        {
            var function1 = @"using System;
using System.Collections.Generic;
public static List<Test> Run(string id)
{
    return null;
}

public class Test 
{
    public string Id { get; set; }
}";

            var tree = CSharpSyntaxTree.ParseText(function1, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
            var references = new MetadataReference[] { MetadataReference.CreateFromFile(typeof(string).Assembly.Location) };
            var compilation = CodeAnalysis.CSharp.CSharpCompilation.Create("test1", references: references).AddSyntaxTrees(tree);

            var signature1 = new Script.Description.CSharpCompilation(compilation).GetEntryPointSignature(new FunctionEntryPointResolver(), null);

            Assert.True(signature1.HasLocalTypeReference);
        }

        [Fact]
        public void Matches_IsTrue_WhenUsingLocalTypesAsDeepGenericArguments()
        {
            var function1 = @"using System;
using System.Threading.Tasks;
using System.Collections.Generic;
public static void Run(string id, ICollection<Task<Test>> test1)
{
    
}

public class Test 
{
    public string Id { get; set; }
}";

            var tree = CSharpSyntaxTree.ParseText(function1, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
            var references = new MetadataReference[] { MetadataReference.CreateFromFile(typeof(string).Assembly.Location) };
            var compilation = CodeAnalysis.CSharp.CSharpCompilation.Create("test1", references: references).AddSyntaxTrees(tree);

            var signature1 = new Script.Description.CSharpCompilation(compilation).GetEntryPointSignature(new FunctionEntryPointResolver(), null);

            Assert.True(signature1.HasLocalTypeReference);
        }

        [Fact]
        public void Matches_IsFalse_WhenNotUsingLocalTypes()
        {
            var function1 = @"using System;
using System.Threading.Tasks;
using System.Collections.Generic;
public static void Run(string id, int test1)
{
    
}

public class Test 
{
    public string Id { get; set; }
}";

            var tree = CSharpSyntaxTree.ParseText(function1, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
            var references = new MetadataReference[] { MetadataReference.CreateFromFile(typeof(string).Assembly.Location) };
            var compilation = CodeAnalysis.CSharp.CSharpCompilation.Create("test1", references: references).AddSyntaxTrees(tree);

            var signature1 = new Script.Description.CSharpCompilation(compilation).GetEntryPointSignature(new FunctionEntryPointResolver(), null);

            Assert.False(signature1.HasLocalTypeReference);
        }
    }
}
