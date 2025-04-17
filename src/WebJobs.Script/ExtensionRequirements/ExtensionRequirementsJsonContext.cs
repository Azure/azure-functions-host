// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script.ExtensionRequirements
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(BundleRequirement[]))]
    [JsonSerializable(typeof(ExtensionStartupTypeRequirement[]))]
    internal partial class ExtensionRequirementsJsonContext : JsonSerializerContext;
}
