// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Configuration;

/// <summary>
/// Supplies root-owned host configuration before ScriptHost configuration and extension discovery.
/// </summary>
public interface IHostJsonConfigurationSourceProvider
{
    /// <summary>
    /// Gets an already accepted configuration source, or <see langword="null"/> to use the local file.
    /// </summary>
    /// <returns>A synchronous, immutable configuration source, or <see langword="null"/> when disabled.</returns>
    /// <remarks>
    /// An enabled provider must fail if configuration has not been accepted. It must not perform file or RPC I/O here.
    /// </remarks>
    IConfigurationSource? GetConfigurationSource();
}
