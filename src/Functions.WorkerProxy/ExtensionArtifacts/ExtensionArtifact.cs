// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.WorkerProxy.ExtensionArtifacts;

/// <summary>
/// Represents an extension artifact payload.
/// </summary>
internal sealed record ExtensionArtifact
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensionArtifact"/> class.
    /// </summary>
    /// <param name="payload">The extension artifact tar archive.</param>
    public ExtensionArtifact(ReadOnlyMemory<byte> payload)
    {
        Payload = payload;
    }

    /// <summary>
    /// Gets the tar archive containing <c>extensions.json</c> and the contents of
    /// the <c>.azurefunctions</c> directory.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; init; }
}
