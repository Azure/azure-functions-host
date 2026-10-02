// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.WorkerProxy.ExtensionArtifacts;

/// <summary>
/// Provides extension artifacts as a compatibility shim for worker SDKs that do not provide them.
/// </summary>
internal interface IExtensionArtifactShim
{
    /// <summary>
    /// Creates an extension artifact from the supplied function app directory.
    /// </summary>
    /// <param name="functionAppDirectory">
    /// The function app directory path.
    /// </param>
    /// <param name="cancellationToken">The token that cancels artifact creation.</param>
    /// <returns>
    /// A task whose result is the artifact, or <see langword="null"/> when the required
    /// artifact inputs are unavailable, which includes an extensions directory that holds
    /// no files.
    /// </returns>
    /// <exception cref="System.ArgumentNullException">
    /// <paramref name="functionAppDirectory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="System.ArgumentException">
    /// <paramref name="functionAppDirectory"/> is empty or whitespace.
    /// </exception>
    /// <exception cref="System.OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled.
    /// </exception>
    /// <exception cref="System.UnauthorizedAccessException">
    /// An artifact input exists but cannot be read. Reported rather than treated as an absent
    /// input, so that an unreadable deployment is not mistaken for one carrying no extensions.
    /// </exception>
    /// <exception cref="System.IO.IOException">
    /// Reading the function app directory failed.
    /// </exception>
    Task<ExtensionArtifact?> CreateAsync(string functionAppDirectory, CancellationToken cancellationToken);
}
