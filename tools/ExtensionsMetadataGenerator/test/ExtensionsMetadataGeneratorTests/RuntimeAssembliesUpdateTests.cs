// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ExtensionsMetadataGeneratorTests
{
    public class RuntimeAssembliesUpdateTests
    {
        public const string ExistingRuntimeAssembliesFileName = "ExistingRuntimeAssemblies.txt";
        public const string GeneratedRuntimeAssembliesFileName = @"runtimeassemblies.txt";
        public const string DiffListFileName = "DiffList.txt";

        [Fact]
        public void VerifyGeneratedRuntimeAssemblies()
        {
            string[] existingRuntimeAssemblies = File.ReadAllLines(ExistingRuntimeAssembliesFileName);
            string[] generatedRuntimeAssemblies = File.ReadAllLines(GeneratedRuntimeAssembliesFileName);

            IEnumerable<string> diffAdded = generatedRuntimeAssemblies.Except(existingRuntimeAssemblies);
            var result = diffAdded.Select(s => $"Added: {s}");

            IEnumerable<string> diffRemoved = existingRuntimeAssemblies.Except(generatedRuntimeAssemblies);
            result = result.Union(diffRemoved.Select(s => $"Removed: {s}"));

            File.WriteAllLines("Result.txt", result);
            Assert.False(result.Any(), $"Generated runtimeassemblies.txt does not match existing list. Please look at {DiffListFileName} for changes. Verify changes and update contents in {ExistingRuntimeAssembliesFileName}");
        }
    }
}
