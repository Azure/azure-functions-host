// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.ExtensionBundle
{
    public class ExtensionBundleContentProviderTests : IDisposable
    {
        private const string StreamContent = "stream content";

        [Fact]
        public async void GetTemplates_BundleNotConfigured_ReturnsNull()
        {
            var contentProvider = new ExtensionBundleContentProvider(new TestExtensionBundleManager(isExtensionBundleConfigured: false), NullLogger<ExtensionBundleContentProvider>.Instance);
            var templates = await contentProvider.GetTemplates();
            Assert.Null(templates);
        }

        [Fact]
        public async void GetTemplates_BundleConfigured_ReturnsTemplates()
        {
            var contentProvider = new ExtensionBundleContentProvider(new TestExtensionBundleManager(bundlePath: "bundlePath", isExtensionBundleConfigured: true), NullLogger<ExtensionBundleContentProvider>.Instance);
            var fileSystemTuple = CreateFileSystem();
            var fileBase = fileSystemTuple.Item3;
            var path = Path.Combine("bundlePath", "StaticContent", "v1", "templates", "templates.json");
            fileBase.Setup(f => f.Exists(path)).Returns(true);
            fileBase.Setup(f => f.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)).Returns(GetReadableStream());
            FileUtility.Instance = fileSystemTuple.Item1.Object;
            var templates = await contentProvider.GetTemplates();
            Assert.NotNull(templates);
            Assert.Equal(templates, StreamContent);
        }

        [Fact]
        public async void GetBindings_BundleNotConfigured_ReturnsNull()
        {
            var contentProvider = new ExtensionBundleContentProvider(new TestExtensionBundleManager(isExtensionBundleConfigured: false), NullLogger<ExtensionBundleContentProvider>.Instance);
            var bindings = await contentProvider.GetBindings();
            Assert.Null(bindings);
        }

        [Fact]
        public async void GetBindings_BundleConfigured_ReturnsBindings()
        {
            var contentProvider = new ExtensionBundleContentProvider(new TestExtensionBundleManager(bundlePath: "bundlePath", isExtensionBundleConfigured: true), NullLogger<ExtensionBundleContentProvider>.Instance);
            var fileSystemTuple = CreateFileSystem();
            var fileBase = fileSystemTuple.Item3;
            var path = Path.Combine("bundlePath", "StaticContent", "v1", "bindings", "bindings.json");
            fileBase.Setup(f => f.Exists(path)).Returns(true);
            fileBase.Setup(f => f.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)).Returns(GetReadableStream());
            FileUtility.Instance = fileSystemTuple.Item1.Object;
            var bindings = await contentProvider.GetBindings();
            Assert.NotNull(bindings);
            Assert.Equal(bindings, StreamContent);
        }

        [Fact]
        public async void GetResources_BundleNotConfigured_ReturnsNull()
        {
            var contentProvider = new ExtensionBundleContentProvider(new TestExtensionBundleManager(isExtensionBundleConfigured: false), NullLogger<ExtensionBundleContentProvider>.Instance);
            var resources = await contentProvider.GetResources();
            Assert.Null(resources);
        }

        [Fact]
        public async void GetResources_BundleConfigured_ReturnsResources()
        {
            var contentProvider = new ExtensionBundleContentProvider(new TestExtensionBundleManager(bundlePath: "bundlePath", isExtensionBundleConfigured: true), NullLogger<ExtensionBundleContentProvider>.Instance);
            var fileSystemTuple = CreateFileSystem();
            var fileBase = fileSystemTuple.Item3;
            var path = Path.Combine("bundlePath", "StaticContent", "v1", "resources", "Resources.json");
            fileBase.Setup(f => f.Exists(path)).Returns(true);
            fileBase.Setup(f => f.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)).Returns(GetReadableStream());
            FileUtility.Instance = fileSystemTuple.Item1.Object;
            var resources = await contentProvider.GetResources();
            Assert.NotNull(resources);
            Assert.Equal(resources, StreamContent);
        }

        [Fact]
        public async void GetResourcesLocale_BundleConfigured_ReturnsResourcesLocale()
        {
            var contentProvider = new ExtensionBundleContentProvider(new TestExtensionBundleManager(bundlePath: "bundlePath", isExtensionBundleConfigured: true), NullLogger<ExtensionBundleContentProvider>.Instance);
            var fileSystemTuple = CreateFileSystem();
            var fileBase = fileSystemTuple.Item3;
            var resourceFileName = "Resources.es-ES.json";

            var path = Path.Combine("bundlePath", "StaticContent", "v1", "resources", resourceFileName);
            fileBase.Setup(f => f.Exists(path)).Returns(true);
            fileBase.Setup(f => f.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)).Returns(GetReadableStream());
            FileUtility.Instance = fileSystemTuple.Item1.Object;
            var resources = await contentProvider.GetResources(resourceFileName);
            Assert.NotNull(resources);
            Assert.Equal(resources, StreamContent);
        }

        public void Dispose()
        {
            FileUtility.Instance = null;
        }

        private Tuple<Mock<IFileSystem>, Mock<DirectoryBase>, Mock<FileBase>> CreateFileSystem()
        {
            var fileSystem = new Mock<IFileSystem>();
            var fileBase = new Mock<FileBase>();
            var dirBase = new Mock<DirectoryBase>();
            fileSystem.SetupGet(f => f.Directory).Returns(dirBase.Object);
            fileSystem.SetupGet(f => f.File).Returns(fileBase.Object);
            return new Tuple<Mock<IFileSystem>, Mock<DirectoryBase>, Mock<FileBase>>(fileSystem, dirBase, fileBase);
        }

        private Stream GetReadableStream()
        {
            var stream = new MemoryStream();
            var streamWriter = new StreamWriter(stream);
            streamWriter.Write(StreamContent);
            streamWriter.Flush();
            stream.Position = 0;
            return stream;
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

            public Task<ExtensionBundleDetails> GetExtensionBundleDetails() => Task.FromResult<ExtensionBundleDetails>(null);

            public Task<string> GetExtensionBundlePath(HttpClient httpClient) => Task.FromResult(_bundlePath);

            public Task<string> GetExtensionBundlePath() => Task.FromResult(_bundlePath);

            public bool IsExtensionBundleConfigured() => _isExtensionBundleConfigured;
        }
    }
}
