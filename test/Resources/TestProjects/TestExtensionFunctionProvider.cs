// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json.Linq;

namespace TestExtensions;

// Linked only into the extension startup and assembly-loading regression projects.
internal sealed class TestExtensionFunctionProvider(params FunctionMetadata[] functions) : IFunctionProvider
{
    public ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors =>
        ImmutableDictionary<string, ImmutableArray<string>>.Empty;

    public Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync() =>
        Task.FromResult(functions.ToImmutableArray());

    internal static FunctionMetadata Http(Type type, string name, string method, params string[] methods)
    {
        var metadata = Create(type, name, method);
        metadata.Bindings.Add(BindingMetadata.Create(new JObject
        {
            ["type"] = "httpTrigger",
            ["direction"] = "in",
            ["name"] = "req",
            ["authLevel"] = "anonymous",
            ["methods"] = new JArray(methods)
        }));
        metadata.Bindings.Add(BindingMetadata.Create(new JObject
        {
            ["type"] = "http",
            ["direction"] = "out",
            ["name"] = "$return"
        }));

        return metadata;
    }

    internal static FunctionMetadata Timer(Type type, string name, string method, string schedule)
    {
        var metadata = Create(type, name, method);
        metadata.Bindings.Add(BindingMetadata.Create(new JObject
        {
            ["type"] = "timerTrigger",
            ["direction"] = "in",
            ["name"] = "timerInfo",
            ["schedule"] = schedule
        }));

        return metadata;
    }

    private static FunctionMetadata Create(Type type, string name, string method)
    {
        var metadata = new FunctionMetadata
        {
            Name = name,
            ScriptFile = type.Assembly.Location,
            FunctionDirectory = Path.GetDirectoryName(type.Assembly.Location),
            EntryPoint = $"{type.FullName}.{method}",
            Language = "DotNetAssembly"
        };

        // These explicit extension registrations retain SDK activation and attribute indexing.
        // Ownership is assigned by the host when it consumes this registered provider.
        metadata.Properties["IsDirect"] = true;

        return metadata;
    }
}
