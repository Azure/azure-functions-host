﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.BindingExtensions;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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
                var manager = GetExtensionsManager(testDir.Path);

                var extensions = await manager.GetExtensions();

                Assert.Equal(0, extensions.Count());
            }
        }

        [Fact]
        public async Task GetExtensions_ReturnsPackages()
        {
            using (var testDir = new TempDirectory())
            {
                var manager = GetExtensionsManager(testDir.Path);

                var extension = new ExtensionPackageReference
                {
                    Id = "Microsoft.Azure.WebJobs.Extensions.Test",
                    Version = "1.0.0"
                };

                await manager.AddExtensions(extension);

                Assert.True(File.Exists(manager.DefaultProjectPath));

                // Create a new manager pointing to the same path:
                manager = GetExtensionsManager(testDir.Path);

                var extensions = await manager.GetExtensions();

                Assert.Equal(1, extensions.Count());

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
                var manager = GetExtensionsManager(testDir.Path);

                var extension = new ExtensionPackageReference
                {
                    Id = "Microsoft.Azure.WebJobs.Samples.Extension",
                    Version = "1.0.0"
                };

                await manager.AddExtensions(extension);

                IEnumerable<ExtensionPackageReference> extensions = await manager.GetExtensions();

                Assert.Equal(1, extensions.Count());

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
                var manager = GetExtensionsManager(testDir.Path);

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

                IEnumerable<ExtensionPackageReference> result = await manager.GetExtensions();

                Assert.Equal(1, result.Count());

                var reference = result.FirstOrDefault(p => string.Equals(p.Id, extensions.First().Id));
                Assert.NotNull(reference);
                Assert.Equal(extensions.First().Version, reference.Version);
            }
        }

        [Fact]
        public async Task DeleteExtension_ExtensionBundleEnabled_DoesNotRemoveExtension()
        {
            using (var testDir = new TempDirectory())
            {
                var manager = GetExtensionsManager(testDir.Path);

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

                // manager with extension bundle configured same path
                manager = GetExtensionsManager(testDir.Path, new TestExtensionBundleManager(testDir.Path, true));
                await manager.DeleteExtensions(extensions[1].Id);

                IEnumerable<ExtensionPackageReference> result = await manager.GetExtensions();

                Assert.Equal(2, result.Count());

                var reference = result.FirstOrDefault(p => string.Equals(p.Id, extensions.First().Id));
                Assert.NotNull(reference);
                Assert.Equal(extensions.First().Version, reference.Version);
            }
        }

        [Fact]
        public async Task AddExtension_ExtensionBundleEnabled_DoesNotAddExtension()
        {
            using (var testDir = new TempDirectory())
            {
                var manager = GetExtensionsManager(testDir.Path, new TestExtensionBundleManager(string.Empty, false));

                var extension = new ExtensionPackageReference
                {
                    Id = "Microsoft.Azure.WebJobs.Extensions.Test",
                    Version = "1.0.0"
                };

                await manager.AddExtensions(extension);

                Assert.True(File.Exists(manager.DefaultProjectPath));

                // Create a new manager pointing to the same path:
                manager = GetExtensionsManager(testDir.Path, new TestExtensionBundleManager(testDir.Path, true));
                extension = new ExtensionPackageReference
                {
                    Id = "Microsoft.Azure.WebJobs.Extensions.Test1",
                    Version = "1.0.0"
                };

                await manager.AddExtensions(extension);

                var extensions = await manager.GetExtensions();

                Assert.Equal(1, extensions.Count());

                var reference = extensions.FirstOrDefault(p => string.Equals(p.Id, extensions.First().Id));
                Assert.NotNull(reference);
                Assert.Equal(extension.Version, reference.Version);
            }
        }

        [Fact]
        public async Task GetExtensions_ExtensionBundleEnabled_ReturnsExtensionsFromBundle()
        {
            using (var testDir = new TempDirectory())
            {
                var manager = GetExtensionsManager(testDir.Path);

                var extension = new ExtensionPackageReference
                {
                    Id = "Microsoft.Azure.WebJobs.Extensions.Test",
                    Version = "1.0.0"
                };

                await manager.AddExtensions(extension);

                Assert.True(File.Exists(manager.DefaultProjectPath));

                var bundlePath = Path.GetDirectoryName(manager.DefaultProjectPath);
                // Create a new manager using extension bundle with same csproj
                manager = GetExtensionsManager(testDir.Path, new TestExtensionBundleManager(bundlePath, true));

                var extensions = await manager.GetExtensions();

                Assert.Equal(1, extensions.Count());

                var reference = extensions.FirstOrDefault(p => string.Equals(p.Id, extensions.First().Id));
                Assert.NotNull(reference);
                Assert.Equal(extension.Version, reference.Version);
            }
        }

        [Fact]
        public async Task GetExtensions_ExtensionBundleEnabled_FailedToFetchBundle_ReturnsEmpty()
        {
            var manager = GetExtensionsManager(string.Empty, new TestExtensionBundleManager(null, true));
            IEnumerable<ExtensionPackageReference> result = await manager.GetExtensions();
            Assert.Equal(0, result.Count());
        }

        private ExtensionsManager GetExtensionsManager(string rootPath, IExtensionBundleManager extensionBundleManager = null)
        {
            extensionBundleManager = extensionBundleManager ?? new TestExtensionBundleManager();
            IOptions<ScriptJobHostOptions> options = new OptionsWrapper<ScriptJobHostOptions>(new ScriptJobHostOptions
            {
                RootScriptPath = rootPath
            });

            var manager = new Mock<ExtensionsManager>(options, NullLogger<ExtensionsManager>.Instance, extensionBundleManager);
            manager.Setup(m => m.ProcessExtensionsProject(It.IsAny<string>()))
                .Returns<string>(a =>
                {
                    File.Copy(Path.Combine(a, ScriptConstants.ExtensionsProjectFileName), Path.Combine(rootPath, ScriptConstants.ExtensionsProjectFileName), true);
                    return Task.CompletedTask;
                });

            return manager.Object;
        }

        private class TestExtensionBundleManager : IExtensionBundleManager
        {
            private readonly string _bundlePath;
            private readonly bool _isExtensionBundleConfigured;

            public TestExtensionBundleManager(string bundlePath = null, bool isExtensionBundleConfigured = false)
            {
                _bundlePath = bundlePath;
                _isExtensionBundleConfigured = isExtensionBundleConfigured;
            }

            public Task<string> GetExtensionBundle(HttpClient httpClient = null) => Task.FromResult(_bundlePath);

            public bool IsExtensionBundleConfigured() => _isExtensionBundleConfigured;
        }
    }
}
