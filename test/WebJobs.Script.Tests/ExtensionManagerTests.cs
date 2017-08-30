// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ExtensionManagerTests
    {
        [Fact]
        public async Task GetExtensions_WithNoPackages_ReturnsEmptyResults()
        {
            using (var testDir = new TempDirectory())
            {
                var manager = new ExtensionsManager(testDir.Path);

                var extensions = await manager.GetExtensions();

                Assert.Equal(0, extensions.Count);
            }
        }

        [Fact]
        public async Task GetExtensions_ReturnsPackges()
        {
            using (var testDir = new TempDirectory())
            {
                var manager = new ExtensionsManager(testDir.Path);

                var extension = new ExtensionPackageReference
                {
                    Id = "Microsoft.Azure.WebJobs.Extensions.Test",
                    Version = "1.0.0"
                };

                await manager.AddExtensions(extension);

                Assert.True(File.Exists(manager.ProjectPath));

                // Create a new manager pointing to the same path:
                manager = new ExtensionsManager(testDir.Path);

                var extensions = await manager.GetExtensions();

                Assert.Equal(1, extensions.Count);

                var reference = extensions.FirstOrDefault(p => string.Equals(p.Id, extensions.First().Id));
                Assert.NotNull(reference);
                Assert.Equal(extension.Version, reference.Version);
            }
        }

        [Fact]
        public async Task GetExtensions_ReturnsNewlyAddedExtension()
        {
            using (var testDir = new TempDirectory())
            {
                var manager = new ExtensionsManager(testDir.Path);

                var extension = new ExtensionPackageReference
                {
                    Id = "Microsoft.Azure.WebJobs.Samples.Extension",
                    Version = "1.0.0"
                };

                await manager.AddExtensions(extension);

                IList<ExtensionPackageReference> extensions = await manager.GetExtensions();

                Assert.Equal(1, extensions.Count);

                var reference = extensions.FirstOrDefault(p => string.Equals(p.Id, extensions.First().Id));
                Assert.NotNull(reference);
                Assert.Equal(extension.Version, reference.Version);
            }
        }

        [Fact]
        public async Task DeleteExtension_RemovesExtension()
        {
            using (var testDir = new TempDirectory())
            {
                var manager = new ExtensionsManager(testDir.Path);

                var extensions = new[]
                {
                    new ExtensionPackageReference
                    {
                        Id = "Microsoft.Azure.WebJobs.Extensions.Test",
                        Version = "1.0.0"
                    },
                    new ExtensionPackageReference
                    {
                        Id = "Microsoft.Azure.WebJobs.Extensions.Test1",
                        Version = "2.0.0"
                    }
                };

                await manager.AddExtensions(extensions);

                await manager.DeleteExtensions(extensions[1].Id);

                IList<ExtensionPackageReference> result = await manager.GetExtensions();

                Assert.Equal(1, result.Count);

                var reference = result.FirstOrDefault(p => string.Equals(p.Id, extensions.First().Id));
                Assert.NotNull(reference);
                Assert.Equal(extensions.First().Version, reference.Version);
            }
        }
    }
}
