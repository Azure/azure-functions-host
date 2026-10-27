// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy.Identity;

/// <summary>
/// Prevents framework request-URI logs from exposing identity queries, including through provider-specific overrides.
/// </summary>
internal sealed class IdentityProxyLoggingOptionsSetup(IOptions<IdentityProxyOptions> identityOptions)
    : IPostConfigureOptions<LoggerFilterOptions>
{
    internal const string RequestCategory = "Microsoft.AspNetCore.Hosting.Diagnostics";

    /// <inheritdoc />
    public void PostConfigure(string? name, LoggerFilterOptions options)
    {
        if (!identityOptions.Value.Enabled)
        {
            return;
        }

        for (int index = 0; index < options.Rules.Count; index++)
        {
            LoggerFilterRule rule = options.Rules[index];
            var originalFilter = rule.Filter;
            options.Rules[index] = new LoggerFilterRule(rule.ProviderName, rule.CategoryName, rule.LogLevel,
                (provider, category, level) => IsAllowed(category, level) && (originalFilter?.Invoke(provider, category, level) ?? true));
        }

        // A provider-specific rule outranks a general category rule. Guard whichever rule is selected instead,
        // including the original MinLevel fallback when no configured rule matches.
        options.Rules.Insert(0, new LoggerFilterRule(null, null, options.MinLevel,
            static (_, category, level) => IsAllowed(category, level)));
    }

    private static bool IsAllowed(string? category, LogLevel level)
    {
        return level >= LogLevel.Warning
            || category is null
            || !category.StartsWith(RequestCategory, StringComparison.OrdinalIgnoreCase);
    }
}
