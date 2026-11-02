// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Parses Host configuration delivered by a worker before channel admission.
/// </summary>
internal interface IWorkerHostConfigurationParser
{
    /// <summary>
    /// Decodes and validates a delivery, creating an immutable Host configuration source.
    /// </summary>
    /// <param name="delivery">The delivered configuration, or <see langword="null"/> when no configuration was delivered.</param>
    /// <returns>
    /// An immutable configuration source for a valid supplied delivery, or <see langword="null"/> only when
    /// <paramref name="delivery"/> is <see langword="null"/>.
    /// </returns>
    /// <exception cref="HostConfigurationException">
    /// The supplied delivery failed or is invalid. The exception contains a safe reason without payload content
    /// or an underlying parser exception; the caller determines how this affects worker admission.
    /// </exception>
    [return: NotNullIfNotNull(nameof(delivery))]
    IConfigurationSource? Parse(HostConfiguration? delivery);
}
