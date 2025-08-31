// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System.Text.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models;

public sealed record FunctionsWorkerContainerAssignmentContext
{
    [JsonPropertyName("assignmentContext")]
    public HostAssignmentContext? AssignmentContext { get; set; }
}