// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Security
{
    public class FileSystemSecretsRepositoryTests
    {
        [Fact]
        public void Constructor_CreatesSecretPathIfNotExists()
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            try
            {
                bool preConstDirExists = Directory.Exists(path);

                var target = new FileSystemSecretsRepository(path);

                bool postConstDirExists = Directory.Exists(path);

                Assert.False(preConstDirExists);
                Assert.True(postConstDirExists);
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path);
                }
            }
        }

        [Theory]
        [InlineData(ScriptSecretsType.Host)]
        [InlineData(ScriptSecretsType.Function)]
        private async Task ReadAsync_ReadsExpectedFile(ScriptSecretsType secretsType)
        {
            using (var directory = new TempDirectory())
            {
                string testContent = "test";
                string testFunctionName = secretsType == ScriptSecretsType.Host ? null : "testfunction";

                File.WriteAllText(Path.Combine(directory.Path, $"{testFunctionName ?? "host"}.json"), testContent);

                var target = new FileSystemSecretsRepository(directory.Path);

                string secretsContent = await target.ReadAsync(secretsType, testFunctionName);

                Assert.Equal(testContent, secretsContent);
            }
        }

        [Theory]
        [InlineData(ScriptSecretsType.Host)]
        [InlineData(ScriptSecretsType.Function)]
        public async Task WriteAsync_CreatesExpectedFile(ScriptSecretsType secretsType)
        {
            using (var directory = new TempDirectory())
            {
                string testContent = "test";
                string testFunctionName = secretsType == ScriptSecretsType.Host ? null : "testfunction";

                var target = new FileSystemSecretsRepository(directory.Path);
                await target.WriteAsync(secretsType, testFunctionName, testContent);

                string filePath = Path.Combine(directory.Path, $"{testFunctionName ?? "host"}.json");
                Assert.True(File.Exists(filePath));
                Assert.Equal(testContent, File.ReadAllText(filePath));
            }
        }

        [Fact]
        public async Task PurgeOldSecrets_RemovesOldAndKeepsCurrentSecrets()
        {
            using (var directory = new TempDirectory())
            {
                Func<int, string> getFilePath = i => Path.Combine(directory.Path, $"{i}.json");

                var sequence = Enumerable.Range(0, 10);
                var files = sequence.Select(i => getFilePath(i)).ToList();

                // Create files
                files.ForEach(f => File.WriteAllText(f, "test"));

                var target = new FileSystemSecretsRepository(directory.Path);

                // Purge, passing even named files as the existing functions
                var currentFunctions = sequence.Where(i => i % 2 == 0).Select(i => i.ToString()).ToList();

                await target.PurgeOldSecretsAsync(currentFunctions, new TestTraceWriter(TraceLevel.Off));

                // Ensure only expected files exist
                Assert.True(sequence.All(i => (i % 2 == 0) == File.Exists(getFilePath(i))));
            }
        }
    }
}