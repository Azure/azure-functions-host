// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

        private Tuple<FunctionSignature, FunctionSignature> GetFunctionSignatures(string function1, string function2)
        {
            var tree1 = CSharpSyntaxTree.ParseText(function1, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
            var tree2 = CSharpSyntaxTree.ParseText(function2, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));

            var references = new MetadataReference[] { MetadataReference.CreateFromFile(typeof(string).Assembly.Location) };

            var compilation1 = new Script.Description.CSharpCompilation(CodeAnalysis.CSharp.CSharpCompilation.Create("test1", references: references)
                   .AddSyntaxTrees(tree1));

            var compilation2 = new Script.Description.CSharpCompilation(CodeAnalysis.CSharp.CSharpCompilation.Create("test2", references: references)
                .AddSyntaxTrees(tree2));

            var signature1 = compilation1.GetEntryPointSignature(new FunctionEntryPointResolver());
            var signature2 = compilation2.GetEntryPointSignature(new FunctionEntryPointResolver());

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

            var signature1 = new Script.Description.CSharpCompilation(compilation).GetEntryPointSignature(new FunctionEntryPointResolver());

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

            var signature1 = new Script.Description.CSharpCompilation(compilation).GetEntryPointSignature(new FunctionEntryPointResolver());

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

            var signature1 = new Script.Description.CSharpCompilation(compilation).GetEntryPointSignature(new FunctionEntryPointResolver());

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

            var signature1 = new Script.Description.CSharpCompilation(compilation).GetEntryPointSignature(new FunctionEntryPointResolver());

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

            var signature1 = new Script.Description.CSharpCompilation(compilation).GetEntryPointSignature(new FunctionEntryPointResolver());

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

            var signature1 = new Script.Description.CSharpCompilation(compilation).GetEntryPointSignature(new FunctionEntryPointResolver());

            Assert.False(signature1.HasLocalTypeReference);
        }
    }
}
