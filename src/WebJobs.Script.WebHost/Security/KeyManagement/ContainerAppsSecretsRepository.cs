// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost;

public class ContainerAppsSecretsRepository : ISecretsRepository
{
    internal const string ContainerAppsSecretsDir = "/run/secrets/functions-keys";

    // host.master = value
    private const string MasterKey = "host.master";
    // host.function.{keyName} = value
    private const string HostFunctionKeyPrefix = "host.function.";
    // host.systemKey.{keyName} = value
    private const string SystemKeyPrefix = "host.systemKey.";
    // functions.{functionName}.{keyName} = value
    private const string FunctionKeyPrefix = "functions.";

    private readonly ILogger<ContainerAppsSecretsRepository> _logger;

    public ContainerAppsSecretsRepository(ILogger<ContainerAppsSecretsRepository> logger)
    {
        _logger = logger;
    }

    // explicitly implementing this to avoid "unused" warnings on build
    event EventHandler<SecretsChangedEventArgs> ISecretsRepository.SecretsChanged
    {
        add { }
        remove { }
    }

    public bool IsEncryptionSupported => false;

    public string Name => nameof(ContainerAppsSecretsRepository);

    public async Task<ScriptSecrets> ReadAsync(ScriptSecretsType type, string functionName)
    {
        if (type == ScriptSecretsType.Function && string.IsNullOrEmpty(functionName))
        {
            throw new ArgumentNullException(nameof(functionName), $"{nameof(functionName)} cannot be null or empty with {nameof(type)} = {nameof(ScriptSecretsType.Function)}");
        }

        return type == ScriptSecretsType.Host ? await ReadHostSecretsAsync() : await ReadFunctionSecretsAsync(functionName?.ToLowerInvariant());
    }

    public Task WriteAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets)
        => throw new NotImplementedException();

    private async Task<ScriptSecrets> ReadHostSecretsAsync()
    {
        var secrets = await GetFromFilesAsync(ContainerAppsSecretsDir);

        HostSecrets hostSecrets = new HostSecrets()
        {
            FunctionKeys = [],
            SystemKeys = []
        };

        foreach (var pair in secrets)
        {
            if (pair.Key.StartsWith(MasterKey, StringComparison.OrdinalIgnoreCase))
            {
                hostSecrets.MasterKey = new Key("master", pair.Value);
            }
            else if (pair.Key.StartsWith(HostFunctionKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                hostSecrets.FunctionKeys.Add(ParseKeyWithPrefix(HostFunctionKeyPrefix, pair.Key, pair.Value));
            }
            else if (pair.Key.StartsWith(SystemKeyPrefix))
            {
                hostSecrets.SystemKeys.Add(ParseKeyWithPrefix(SystemKeyPrefix, pair.Key, pair.Value));
            }
        }

        // Always return a HostSecrets object, even if empty. This will prevent the SecretManager from thinking
        // it needs to create and persist new secrets, which is not supported in Container Apps.
        return hostSecrets;
    }

    private async Task<ScriptSecrets> ReadFunctionSecretsAsync(string functionName)
    {
        var secrets = await GetFromFilesAsync(ContainerAppsSecretsDir);

        var prefix = $"{FunctionKeyPrefix}{functionName}.";

        var functionSecrets = new FunctionSecrets()
        {
            Keys = secrets
                .Where(p => p.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(p => ParseKeyWithPrefix(prefix, p.Key, p.Value))
                .ToList()
        };

        // Always return a FunctionSecrets object, even if empty. This will prevent the SecretManager from thinking
        // it needs to create and persist new secrets, which is not supported in Container Apps.
        return functionSecrets;
    }

    private async Task<IDictionary<string, string>> GetFromFilesAsync(string path)
    {
        string[] files = await FileUtility.GetFilesAsync(path, "*");
        var secrets = new Dictionary<string, string>(files.Length);

        StringBuilder sb = new StringBuilder("Loaded secrets from files:");

        foreach (var file in files)
        {
            secrets.Add(Path.GetFileName(file), await FileUtility.ReadAsync(file));
            sb.AppendLine($"  {file}");
        }

        _logger.LogDebug(sb.ToString());
        return secrets;
    }

    public Task WriteSnapshotAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets)
        => throw new NotImplementedException();

    public Task PurgeOldSecretsAsync(IList<string> currentFunctions, ILogger logger)
        => throw new NotImplementedException();

    public Task<string[]> GetSecretSnapshots(ScriptSecretsType type, string functionName)
        => throw new NotImplementedException();

    private static Key ParseKeyWithPrefix(string prefix, string key, string value)
        => new(key.Substring(prefix.Length), value);
}