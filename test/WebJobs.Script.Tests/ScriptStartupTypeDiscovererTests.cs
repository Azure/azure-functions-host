// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Azure.WebJobs.Extensions;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.Storage;
using Microsoft.Azure.WebJobs.Script.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.Models;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ScriptStartupTypeDiscovererTests
    {
        [Fact]
        public void GetExtensionsStartupTypes_FiltersBuiltinExtensions()
        {
            var references = new[]
            {
                new ExtensionReference { Name = "Http", TypeName = typeof(HttpWebJobsStartup).AssemblyQualifiedName },
                new ExtensionReference { Name = "Timers", TypeName = typeof(ExtensionsWebJobsStartup).AssemblyQualifiedName },
                new ExtensionReference { Name = "Storage", TypeName = typeof(AzureStorageWebJobsStartup).AssemblyQualifiedName },
            };

            var extensions = new JObject
            {
                { "extensions", JArray.FromObject(references) }
            };

            using (var directory = new TempDirectory())
            {
                var binPath = Path.Combine(directory.Path, "bin");
                Directory.CreateDirectory(binPath);

                void CopyToBin(string path)
                {
                    File.Copy(path, Path.Combine(binPath, Path.GetFileName(path)));
                }

                CopyToBin(typeof(HttpWebJobsStartup).Assembly.Location);
                CopyToBin(typeof(ExtensionsWebJobsStartup).Assembly.Location);
                CopyToBin(typeof(AzureStorageWebJobsStartup).Assembly.Location);

                File.WriteAllText(Path.Combine(binPath, "extensions.json"), extensions.ToString());

                var testLogger = new TestLogger("test");
                var discoverer = new ScriptStartupTypeDiscoverer(directory.Path, testLogger);

                // Act
                var types = discoverer.GetExtensionsStartupTypes();
                var traces = testLogger.GetLogMessages();

                // Assert
                Assert.Single(types);
                Assert.Equal(typeof(AzureStorageWebJobsStartup).FullName, types.Single().FullName);
                Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"The extension startup type '{references[0].TypeName}' belongs to a builtin extension")));
                Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"The extension startup type '{references[1].TypeName}' belongs to a builtin extension")));
            }
        }
    }
}
