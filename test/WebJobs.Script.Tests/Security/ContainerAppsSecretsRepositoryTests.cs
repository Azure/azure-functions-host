// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests;

public class ContainerAppsSecretsRepositoryTests : IDisposable
{
    private Dictionary<string, Func<MemoryStream>> _fileContentMap;
    private ContainerAppsSecretsRepository _repo;

    public ContainerAppsSecretsRepositoryTests()
    {
        // Mock the file system to return predefined secrets
        var mockFileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var mockFile = new Mock<FileBase>(MockBehavior.Strict);
        var mockDirectory = new Mock<DirectoryBase>(MockBehavior.Strict);

        // Setup directory and file existence
        mockDirectory
            .Setup(d => d.Exists(ContainerAppsSecretsRepository.ContainerAppsSecretsDir))
            .Returns(() => _fileContentMap is not null);
        mockFileSystem.SetupGet(fs => fs.Directory).Returns(mockDirectory.Object);
        mockFileSystem.SetupGet(fs => fs.File).Returns(mockFile.Object);

        // Return all files when asked
        mockDirectory
            .Setup(d => d.GetFiles(ContainerAppsSecretsRepository.ContainerAppsSecretsDir, "*"))
            .Returns(() =>
            {
                // treat a null map as a missing directory
                if (_fileContentMap is not null)
                {
                    return _fileContentMap.Keys.ToArray();
                }

                throw new DirectoryNotFoundException();
            });

        // Setup file existence checks
        mockFile
            .Setup(f => f.Exists(It.IsAny<string>()))
            .Returns((string f) => _fileContentMap.ContainsKey(f));

        // Return content when asked
        mockFile
            .Setup(f => f.Open(It.IsAny<string>(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            .Returns((string f, FileMode _, FileAccess _, FileShare _) => _fileContentMap[f]());

        FileUtility.Instance = mockFileSystem.Object;

        _repo = new ContainerAppsSecretsRepository(NullLogger<ContainerAppsSecretsRepository>.Instance);
    }

    [Fact]
    public async Task Read_Host_Secrets()
    {
        _fileContentMap = new()
        {
            { "/run/secrets/functions-keys/host.master", () => GetStream("mk") },
            { "/run/secrets/functions-keys/host.function.default", () => GetStream("hfd") },
            { "/run/secrets/functions-keys/host.function.key1", () => GetStream("hfk1") },
            { "/run/secrets/functions-keys/host.systemKey.key1", () => GetStream("hsk1") },
        };

        var result = await _repo.ReadAsync(ScriptSecretsType.Host, null);

        var hostSecrets = result as HostSecrets;
        Assert.NotNull(hostSecrets);
        Assert.Equal("mk", hostSecrets.MasterKey.Value);
        Assert.Equal("hfd", hostSecrets.GetFunctionKey("default", HostKeyScopes.FunctionKeys).Value);
        Assert.Equal("hfk1", hostSecrets.GetFunctionKey("Key1", HostKeyScopes.FunctionKeys).Value);
        Assert.Equal("hsk1", hostSecrets.GetFunctionKey("Key1", HostKeyScopes.SystemKeys).Value);
    }

    [Fact]
    public async Task Read_Host_Secrets_MissingDirectory()
    {
        _fileContentMap = null;

        var result = await _repo.ReadAsync(ScriptSecretsType.Host, null);

        var hostSecrets = result as HostSecrets;
        Assert.NotNull(hostSecrets);
        Assert.Null(hostSecrets.MasterKey);
        Assert.Empty(hostSecrets.FunctionKeys);
        Assert.Empty(hostSecrets.SystemKeys);
    }

    [Fact]
    public async Task Read_Function_Secrets()
    {
        _fileContentMap = new()
        {
            { "/run/secrets/functions-keys/functions.function1.key1", () => GetStream("f1k1") },
            { "/run/secrets/functions-keys/functions.function1.key2", () => GetStream("f1k2") },
            { "/run/secrets/functions-keys/functions.function2.key1", () => GetStream("f2k1") },
            { "/run/secrets/functions-keys/functions.function2.key2", () => GetStream("f2k2") }
        };

        var result = await _repo.ReadAsync(ScriptSecretsType.Function, "function1");

        var functionSecrets = result as FunctionSecrets;
        Assert.NotNull(functionSecrets);
        Assert.Equal("f1k1", functionSecrets.GetFunctionKey("Key1", "function1").Value);
        Assert.Equal("f1k2", functionSecrets.GetFunctionKey("key2", "Function1").Value);

        result = await _repo.ReadAsync(ScriptSecretsType.Function, "function2");
        functionSecrets = result as FunctionSecrets;
        Assert.NotNull(functionSecrets);
        Assert.Equal("f2k1", functionSecrets.GetFunctionKey("Key1", "funcTion2").Value);
        Assert.Equal("f2k2", functionSecrets.GetFunctionKey("key2", "function2").Value);
    }

    [Fact]
    public async Task Read_Function_Secrets_MissingDirectory()
    {
        _fileContentMap = null;

        var result = await _repo.ReadAsync(ScriptSecretsType.Function, "function1");

        var functionSecrets = result as FunctionSecrets;
        Assert.NotNull(functionSecrets);
        Assert.Empty(functionSecrets.Keys);
    }

    [Fact]
    public async Task No_HostKeys_Returns_Empty_HostSecrets()
    {
        _fileContentMap = [];

        var result = await _repo.ReadAsync(ScriptSecretsType.Host, null);

        var hostSecrets = result as HostSecrets;
        Assert.NotNull(hostSecrets);
        Assert.Null(hostSecrets.MasterKey);
        Assert.Empty(hostSecrets.FunctionKeys);
        Assert.Empty(hostSecrets.SystemKeys);
    }

    [Fact]
    public async Task No_FunctionKeys_Returns_Empty_FunctionSecrets()
    {
        _fileContentMap = [];

        var result = await _repo.ReadAsync(ScriptSecretsType.Function, "function1");

        var hostSecrets = result as FunctionSecrets;
        Assert.NotNull(hostSecrets);
        Assert.Empty(hostSecrets.Keys);
    }

    [Fact]
    public async Task SecretManager_DoesNotCreate_HostSecrets()
    {
        // no keys; we don't want the SecretManager to try to create new ones
        _fileContentMap = [];

        var testEnvironment = new TestEnvironment();
        var mockHostNameProvider = new Mock<HostNameProvider>(MockBehavior.Strict, testEnvironment);
        var startupContextProvider = new StartupContextProvider(testEnvironment, NullLogger<StartupContextProvider>.Instance);

        var secretManager = new SecretManager(_repo, NullLogger.Instance, new TestMetricsLogger(), mockHostNameProvider.Object, startupContextProvider);

        var hostSecrets = await secretManager.GetHostSecretsAsync();

        Assert.NotNull(hostSecrets);
        Assert.Null(hostSecrets.MasterKey);
        Assert.Empty(hostSecrets.FunctionKeys);
        Assert.Empty(hostSecrets.SystemKeys);
    }

    [Fact]
    public async Task SecretManager_DoesNotCreate_FunctionSecrets()
    {
        // no keys; we don't want the SecretManager to try to create new ones
        _fileContentMap = [];

        var testEnvironment = new TestEnvironment();
        var mockHostNameProvider = new Mock<HostNameProvider>(MockBehavior.Strict, testEnvironment);
        var startupContextProvider = new StartupContextProvider(testEnvironment, NullLogger<StartupContextProvider>.Instance);

        var secretManager = new SecretManager(_repo, NullLogger.Instance, new TestMetricsLogger(), mockHostNameProvider.Object, startupContextProvider);

        var functionSecrets = await secretManager.GetFunctionSecretsAsync("function1");

        Assert.NotNull(functionSecrets);
        Assert.Empty(functionSecrets);
    }

    private static MemoryStream GetStream(string content)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(content));
    }

    public void Dispose()
    {
        FileUtility.Instance = null;
    }
}