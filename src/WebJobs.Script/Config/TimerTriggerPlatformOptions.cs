// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Configuration;

/// <summary>
/// Options that describe platform-level capabilities for timer trigger bindings.
/// </summary>
public class TimerTriggerPlatformOptions
{
    /// <summary>
    /// Gets or sets the behavior when a non-CRON schedule (e.g. TimeSpan) is detected
    /// for a timer trigger.
    /// </summary>
    public NonCronScheduleBehavior NonCronScheduleBehavior { get; set; } = NonCronScheduleBehavior.Allow;
}
