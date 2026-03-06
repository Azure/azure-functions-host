// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Configuration;

/// <summary>
/// Specifies the behavior when a non-CRON schedule expression is detected for a timer trigger.
/// </summary>
public enum NonCronScheduleBehavior
{
    /// <summary>
    /// Non-CRON expressions (e.g. TimeSpan) are allowed without any validation or warning.
    /// </summary>
    Allow,

    /// <summary>
    /// A diagnostic warning is logged when a non-CRON expression is used, but the timer is
    /// still allowed to start. This is intended for platforms where the scale controller does
    /// not support non-CRON expressions and customers should migrate.
    /// </summary>
    Warn,

    /// <summary>
    /// An error is thrown when a non-CRON expression is used, preventing the timer from starting.
    /// </summary>
    Error
}
