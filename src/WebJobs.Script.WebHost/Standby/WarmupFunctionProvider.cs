// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost;

internal sealed class WarmupFunctionProvider(
    IOptionsMonitor<ScriptApplicationHostOptions> applicationOptions,
    IOptionsMonitor<StandbyOptions> standbyOptions) : IFunctionProvider
{
    public ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors =>
        ImmutableDictionary<string, ImmutableArray<string>>.Empty;

    public Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync()
    {
        var options = applicationOptions.CurrentValue;
        if (!options.IsStandbyConfiguration || !standbyOptions.CurrentValue.SupportsPlaceholderScriptHost)
        {
            return Task.FromResult(ImmutableArray<FunctionMetadata>.Empty);
        }

        var metadata = new FunctionMetadata
        {
            Name = WarmUpConstants.FunctionName,
            FunctionDirectory = Path.Combine(options.ScriptPath, WarmUpConstants.FunctionName),
            ScriptFile = $"assembly:{typeof(WarmupFunctionProvider).Assembly.FullName}",
            EntryPoint = $"{typeof(WarmupFunctionProvider).FullName}.{nameof(Run)}",
            Language = DotNetScriptTypes.DotNetAssembly
        };
        var config = JObject.Parse(FileUtility.ReadResourceString(
            $"{ScriptConstants.ResourcePath}.Functions.{WarmUpConstants.FunctionName}.function.json"));
        foreach (JObject binding in (JArray)config["bindings"])
        {
            metadata.Bindings.Add(BindingMetadata.Create(binding));
        }

        return Task.FromResult(ImmutableArray.Create(metadata));
    }

    public static async Task<IActionResult> Run(HttpRequest req, ILogger log)
    {
        // Preserve the placeholder's JSON, dynamic binding and collection warmup work.
        log.LogInformation("WarmUp function invoked.");
        string name = req.Query["name"];
        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        dynamic data = JsonConvert.DeserializeObject(requestBody);
        name = name ?? data?.name ?? string.Empty;

        HashSet<Guid> values = [Guid.NewGuid(), Guid.NewGuid()];
        foreach (var value in values)
        {
            _ = value.GetHashCode();
        }

        return new OkObjectResult("WarmUp complete.");
    }
}
