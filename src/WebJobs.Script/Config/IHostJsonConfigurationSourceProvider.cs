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
    /// Gets the selected snapshot, or <see langword="null"/> when the legacy file source was selected.
    /// </summary>
    /// <returns>A synchronous, immutable configuration source, or <see langword="null"/> for the legacy file source.</returns>
    /// <remarks>
    /// The source choice lasts for the root lifetime. This method performs no file or RPC I/O.
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">No worker has been admitted to select a source.</exception>
    IConfigurationSource? GetConfigurationSource();
}
