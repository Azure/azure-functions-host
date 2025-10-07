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
        if (type == ScriptSecretsType.Function)
        {
            ArgumentException.ThrowIfNullOrEmpty(functionName);
        }

        return type == ScriptSecretsType.Host ? await ReadHostSecretsAsync() : await ReadFunctionSecretsAsync(functionName.ToLowerInvariant());
    }

    public Task WriteAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets)
        => throw new NotSupportedException($"The {nameof(ContainerAppsSecretsRepository)} is read-only.");

    private async Task<ScriptSecrets> ReadHostSecretsAsync()
    {
        IDictionary<string, string> secrets = await GetFromFilesAsync(ContainerAppsSecretsDir);

        HostSecrets hostSecrets = new()
        {
            FunctionKeys = [],
            SystemKeys = []
        };

        foreach (var pair in secrets)
        {
            if (string.Compare(pair.Key, MasterKey) == 0)
            {
                hostSecrets.MasterKey = new Key("master", pair.Value);
            }
            else if (pair.Key.StartsWith(HostFunctionKeyPrefix))
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
        if (!FileUtility.DirectoryExists(path))
        {
            _logger.LogDebug("Secrets path '{path}' does not exist.", path);
            return new Dictionary<string, string>();
        }

        string[] files = await FileUtility.GetFilesAsync(path, "*");
        var secrets = new Dictionary<string, string>(files.Length);

        StringBuilder sb = new("Loaded secrets from files:");

        foreach (var file in files)
        {
            secrets.Add(Path.GetFileName(file), await FileUtility.ReadAsync(file));
            sb.AppendLine($"  {file}");
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(sb.ToString());
        }

        return secrets;
    }

    /// <summary>
    /// no-op - allow stale secrets to remain.
    /// </summary>
    public Task PurgeOldSecretsAsync(IList<string> currentFunctions, ILogger logger)
        => Task.CompletedTask;

    /// <summary>
    /// Runtime is not responsible for encryption so this code will never be executed.
    /// </summary>
    public Task WriteSnapshotAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets)
        => throw new NotSupportedException($"The {nameof(ContainerAppsSecretsRepository)} is read-only.");

    /// <summary>
    /// Runtime is not responsible for encryption so this code will never be executed.
    /// </summary>
    public Task<string[]> GetSecretSnapshots(ScriptSecretsType type, string functionName)
        => throw new NotSupportedException($"The {nameof(ContainerAppsSecretsRepository)} is read-only.");

    private static Key ParseKeyWithPrefix(string prefix, string key, string value)
        => new(key.Substring(prefix.Length), value);
}