// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Config;

/// <summary>
/// Defines listener targeting selected by service registration, never by customer configuration binding.
/// </summary>
internal sealed class FunctionGroupListenerOptions
{
    /// <summary>
    /// Gets or sets whether serving listeners are restricted by the configured target group.
    /// Validation runtimes remain non-serving even when this restriction is disabled.
    /// </summary>
    public bool RestrictListenersToTargetGroup { get; set; } = true;
}
