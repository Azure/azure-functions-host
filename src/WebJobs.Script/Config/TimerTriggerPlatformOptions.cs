// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.WebJobs.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Configuration;

/// <summary>
/// Options that describe platform-level capabilities for timer trigger bindings.
/// </summary>
public sealed class TimerTriggerPlatformOptions : IOptionsFormatter
{
    /// <summary>
    /// Gets or sets the behavior when a non-CRON schedule (e.g. TimeSpan) is detected
    /// for a timer trigger.
    /// </summary>
    public NonCronScheduleBehavior NonCronScheduleBehavior { get; set; } = NonCronScheduleBehavior.Allow;

    /// <inheritdoc/>
    public string Format()
    {
        return JsonSerializer.Serialize(this, typeof(TimerTriggerPlatformOptions), TimerTriggerPlatformOptionsJsonContext.Default);
    }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization, WriteIndented = true)]
[JsonSerializable(typeof(TimerTriggerPlatformOptions))]
internal partial class TimerTriggerPlatformOptionsJsonContext : JsonSerializerContext;
