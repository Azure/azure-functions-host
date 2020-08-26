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
        public const string GeneratedRuntimeAssembliesFileName = "runtimeassemblies.txt";

        [Fact]
        public void VerifyGeneratedRuntimeAssemblies()
        {
            string[] existingRuntimeAssemblies = File.ReadAllLines(ExistingRuntimeAssembliesFileName);
            string[] generatedRuntimeAssemblies = File.ReadAllLines(GeneratedRuntimeAssembliesFileName);

            IEnumerable<string> diffAdded = generatedRuntimeAssemblies.Except(existingRuntimeAssemblies);
            var result = diffAdded.Select(s => $"Added: {s}");

            IEnumerable<string> diffRemoved = existingRuntimeAssemblies.Except(generatedRuntimeAssemblies);
            result = result.Union(diffRemoved.Select(s => $"Removed: {s}"));

            string diffString = string.Join(";", result);

            Assert.False(result.Any(), $"Generated runtimeassemblies.txt file:{GeneratedRuntimeAssembliesFileName} does not match existing list:{ExistingRuntimeAssembliesFileName}.\n Review Diff list:\n{diffString}\n Verify changes and update contents in {ExistingRuntimeAssembliesFileName}");
        }
    }
}
