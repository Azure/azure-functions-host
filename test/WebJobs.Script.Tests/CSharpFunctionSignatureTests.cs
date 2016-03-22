// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace WebJobs.Script.Tests
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

            var tree1 = CSharpSyntaxTree.ParseText(function1, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
            var tree2 = CSharpSyntaxTree.ParseText(function2, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));

            var references = new MetadataReference[] { MetadataReference.CreateFromFile(typeof(string).Assembly.Location) };

            var compilation1 = CSharpCompilation.Create("test1", references: references).AddSyntaxTrees(tree1);
            var compilation2 = CSharpCompilation.Create("test2", references: references).AddSyntaxTrees(tree1);

            var signature1 = CSharpFunctionSignature.FromCompilation(compilation1,  new FunctionEntryPointResolver());
            var signature2 = CSharpFunctionSignature.FromCompilation(compilation2, new FunctionEntryPointResolver());

            Assert.True(signature1.Equals(signature2));
            Assert.Equal(signature1.GetHashCode(), signature2.GetHashCode());
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

            var tree1 = CSharpSyntaxTree.ParseText(function1, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
            var tree2 = CSharpSyntaxTree.ParseText(function2, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));

            var references = new MetadataReference[] { MetadataReference.CreateFromFile(typeof(string).Assembly.Location) };

            var compilation1 = CSharpCompilation.Create("test1", references: references).AddSyntaxTrees(tree1);
            var compilation2 = CSharpCompilation.Create("test2", references: references).AddSyntaxTrees(tree1);

            var signature1 = CSharpFunctionSignature.FromCompilation(compilation1, new FunctionEntryPointResolver());
            var signature2 = CSharpFunctionSignature.FromCompilation(compilation2, new FunctionEntryPointResolver());

            Assert.True(signature1.Equals(signature2));
            Assert.Equal(signature1.GetHashCode(), signature2.GetHashCode());
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
            var compilation = CSharpCompilation.Create("test1", references: references).AddSyntaxTrees(tree);

            var signature1 = CSharpFunctionSignature.FromCompilation(compilation, new FunctionEntryPointResolver());

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
            var compilation = CSharpCompilation.Create("test1", references: references).AddSyntaxTrees(tree);

            var signature1 = CSharpFunctionSignature.FromCompilation(compilation, new FunctionEntryPointResolver());

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
            var compilation = CSharpCompilation.Create("test1", references: references).AddSyntaxTrees(tree);

            var signature1 = CSharpFunctionSignature.FromCompilation(compilation, new FunctionEntryPointResolver());

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
            var compilation = CSharpCompilation.Create("test1", references: references).AddSyntaxTrees(tree);

            var signature1 = CSharpFunctionSignature.FromCompilation(compilation, new FunctionEntryPointResolver());

            Assert.False(signature1.HasLocalTypeReference);
        }
    }
}
