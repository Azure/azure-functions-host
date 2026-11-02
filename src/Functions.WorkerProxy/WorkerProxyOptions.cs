// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Defines the WorkerProxy pod identity and listener settings.
/// </summary>
internal sealed class WorkerProxyOptions
{
    /// <summary>
    /// The configuration section containing WorkerProxy settings.
    /// </summary>
    public const string SectionName = "WorkerProxy";

    /// <summary>
    /// Gets or sets the required platform-provided pod name.
    /// </summary>
    /// <remarks>
    /// Populated from WEBSITE_POD_NAME, falling back to CONTAINER_NAME.
    /// An explicitly configured WorkerProxy:PodName does not override the platform identity.
    /// </remarks>
    public string PodName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the HTTP/1 management listener port.
    /// </summary>
    public int ManagementPort { get; set; } = 80;

    /// <summary>
    /// Gets or sets the runtime-facing HTTPS/2 FunctionRpc listener port.
    /// </summary>
    public int RuntimeGrpcPort { get; set; } = 50053;

    /// <summary>
    /// Gets or sets the worker-facing HTTP/2 FunctionRpc listener port.
    /// </summary>
    public int WorkerGrpcPort { get; set; } = 50054;

    /// <summary>
    /// Gets or sets the runtime-facing HTTPS/1 forwarding listener port.
    /// </summary>
    public int HttpPort { get; set; } = 28080;

    /// <summary>
    /// Gets or sets the PEM certificate provisioned by the container startup script.
    /// </summary>
    public string CertificatePath { get; set; } = "/etc/ssl/workerproxy/server.crt";

    /// <summary>
    /// Gets or sets the unencrypted PEM private key provisioned by the container startup script.
    /// </summary>
    public string CertificateKeyPath { get; set; } = "/etc/ssl/workerproxy/server.key";

    /// <summary>
    /// Gets or sets an optional explicit worker HTTP destination.
    /// </summary>
    public string? WorkerHttpEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the externally reachable HTTPS origin advertised to the runtime.
    /// Used when assignment does not supply an allocated origin. HTTP-capable workers require one of these trusted sources.
    /// Each WorkerProxy owns a dedicated
    /// address and port, so this URL must not include a path prefix. It must route to
    /// <see cref="HttpPort"/>, but its external port may differ because of platform port mapping.
    /// </summary>
    public string? HttpProxyEndpoint { get; set; }
}
