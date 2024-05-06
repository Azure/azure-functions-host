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
        public const string ExistingRuntimeAssembliesFilePrefix = "ExistingRuntimeAssemblies";
        public const string GeneratedRuntimeAssembliesFilePrefix = "runtimeassemblies";

        [Theory]
        [InlineData("net8")]
        [InlineData("net6")]
        public void VerifyGeneratedRuntimeAssemblies(string targetFramework)
        {
            string existingRuntimeAssembliesFileName = $"{ExistingRuntimeAssembliesFilePrefix}-{targetFramework}.txt";
            string generatedRuntimeAssembliesFileName = $"{GeneratedRuntimeAssembliesFilePrefix}-{targetFramework}.txt";

            string[] existingRuntimeAssemblies = File.ReadAllLines(existingRuntimeAssembliesFileName);
            string[] generatedRuntimeAssemblies = File.ReadAllLines(generatedRuntimeAssembliesFileName);

            IEnumerable<string> diffAdded = generatedRuntimeAssemblies.Except(existingRuntimeAssemblies);
            var result = diffAdded.Select(s => $"Added: {s}");

            IEnumerable<string> diffRemoved = existingRuntimeAssemblies.Except(generatedRuntimeAssemblies);
            result = result.Union(diffRemoved.Select(s => $"Removed: {s}"));

            string diffString = string.Join(";", result);

            Assert.False(result.Any(), $"Generated file:{generatedRuntimeAssembliesFileName} does not match existing list:{existingRuntimeAssembliesFileName}.\n Review Diff list:\n{diffString}\n Verify changes and update contents in {existingRuntimeAssembliesFileName}");
        }
    }
}
