// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Host;
using Microsoft.Extensions.Logging;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.ExtensionBundle;

public sealed class DefaultExtensionPathProviderTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly DefaultExtensionPathProvider _provider = new();

    public void Dispose() => _directory.Dispose();

    [Theory]
    [InlineData("bin", false)]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData(".azurefunctions", false)]
    public void ResolvesExistingApplicationLayouts(string relativeMetadataDirectory, bool separateProbingDirectory)
    {
        string metadataDirectory = Path.Combine(_directory.Path, relativeMetadataDirectory);
        Directory.CreateDirectory(metadataDirectory);
        File.WriteAllText(Path.Combine(metadataDirectory, "extensions.json"), "{}");
        string probingDirectory = null;
        if (separateProbingDirectory)
        {
            probingDirectory = Path.Combine(_directory.Path, ".azurefunctions");
            Directory.CreateDirectory(probingDirectory);
        }

        Assert.True(_provider.TryResolveExtensionsMetadataPath(_directory.Path, out string metadata, out string probing));
        Assert.Equal(metadataDirectory, metadata);
        Assert.Equal(probingDirectory, probing);
        Assert.True(_provider.UseExtensionBundles);
    }

    [Fact]
    public void BinMetadataTakesPrecedenceOverRootMetadata()
    {
        string bin = Path.Combine(_directory.Path, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "extensions.json"), "{}");
        File.WriteAllText(Path.Combine(_directory.Path, "extensions.json"), "{}");

        Assert.True(_provider.TryResolveExtensionsMetadataPath(_directory.Path, out string metadata, out string probing));
        Assert.Equal(bin, metadata);
        Assert.Null(probing);
    }

    [Fact]
    public void MissingMetadataReturnsNoLocation()
    {
        Assert.False(_provider.TryResolveExtensionsMetadataPath(_directory.Path, out string metadata, out string probing));
        Assert.Null(metadata);
        Assert.Null(probing);
        Assert.Throws<ArgumentNullException>(() => _provider.TryResolveExtensionsMetadataPath(null, out _, out _));
    }

    [Fact]
    public void FolderValidatorUsesTheConfiguredProbingDirectory()
    {
        string metadata = _directory.Path;
        string probing = Path.Combine(_directory.Path, "configured-extensions");
        Directory.CreateDirectory(probing);
        Mock<IExtensionPathProvider> provider = new(MockBehavior.Strict);
        provider.Setup(value => value.TryResolveExtensionsMetadataPath(_directory.Path, out metadata, out probing)).Returns(true);
        Mock<ILogger> logger = new(MockBehavior.Strict);
        MissingAzureFunctionsFolderValidator validator = new(provider.Object);

        validator.Validate(new() { RootScriptPath = _directory.Path }, new TestEnvironment(), logger.Object);

        logger.VerifyNoOtherCalls();
        provider.VerifyAll();
    }
}
