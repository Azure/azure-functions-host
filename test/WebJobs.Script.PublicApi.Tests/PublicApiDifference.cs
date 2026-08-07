// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.PublicApi.Tests;

/// <summary>
/// Compares a checked-in baseline against the current compiled public API, reporting added,
/// removed, and changed signatures separately.
/// </summary>
internal sealed class PublicApiDifference
{
    private PublicApiDifference(
        IReadOnlyList<string> added,
        IReadOnlyList<string> removed,
        IReadOnlyList<PublicApiChange> changed)
    {
        Added = added;
        Removed = removed;
        Changed = changed;
    }

    /// <summary>
    /// Gets the signatures present in the current API but missing from the baseline.
    /// </summary>
    public IReadOnlyList<string> Added { get; }

    /// <summary>
    /// Gets the signatures present in the baseline but missing from the current API.
    /// </summary>
    public IReadOnlyList<string> Removed { get; }

    /// <summary>
    /// Gets the signatures whose declaration changed while keeping the same member identity.
    /// </summary>
    public IReadOnlyList<PublicApiChange> Changed { get; }

    /// <summary>
    /// Gets a value indicating whether the baseline matches the current API exactly.
    /// </summary>
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;

    /// <summary>
    /// Compares baseline lines with current lines.
    /// </summary>
    /// <param name="baseline">The checked-in baseline lines.</param>
    /// <param name="current">The current compiled API lines.</param>
    /// <returns>The difference.</returns>
    public static PublicApiDifference Compare(IEnumerable<string> baseline, IEnumerable<string> current)
    {
        var baselineSet = new HashSet<string>(baseline, StringComparer.Ordinal);
        var currentSet = new HashSet<string>(current, StringComparer.Ordinal);

        List<string> removed = baselineSet.Except(currentSet, StringComparer.Ordinal).ToList();
        List<string> added = currentSet.Except(baselineSet, StringComparer.Ordinal).ToList();

        var changed = new List<PublicApiChange>();

        foreach (IGrouping<string, string> group in removed
            .GroupBy(PublicApiRecord.GetChangeKey, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToArray())
        {
            string[] replacements = added.Where(line => string.Equals(PublicApiRecord.GetChangeKey(line), group.Key, StringComparison.Ordinal)).ToArray();
            if (replacements.Length != 1)
            {
                continue;
            }

            changed.Add(new PublicApiChange(group.Single(), replacements[0]));
            removed.Remove(group.Single());
            added.Remove(replacements[0]);
        }

        return new PublicApiDifference(
            added.OrderBy(line => line, StringComparer.Ordinal).ToArray(),
            removed.OrderBy(line => line, StringComparer.Ordinal).ToArray(),
            changed.OrderBy(change => change.Baseline, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Renders a reviewable report of the difference.
    /// </summary>
    /// <param name="assemblyName">The assembly the difference belongs to.</param>
    /// <returns>The report text.</returns>
    public string Describe(string assemblyName)
    {
        var message = new StringBuilder()
            .AppendLine($"The compiled public API of '{assemblyName}' does not match its checked-in baseline.")
            .AppendLine("Review the change, then refresh the baselines with: eng/script/update-public-api-baselines.ps1")
            .AppendLine();

        AppendSection(message, "Removed", Removed);
        AppendSection(message, "Added", Added);

        message.AppendLine($"Changed ({Changed.Count}):");
        if (Changed.Count == 0)
        {
            message.AppendLine("  (none)");
        }
        else
        {
            foreach (PublicApiChange change in Changed)
            {
                message.AppendLine($"  - {change.Baseline}");
                message.AppendLine($"  + {change.Current}");
            }
        }

        return message.ToString();
    }

    private static void AppendSection(StringBuilder message, string heading, IReadOnlyList<string> lines)
    {
        message.AppendLine($"{heading} ({lines.Count}):");

        if (lines.Count == 0)
        {
            message.AppendLine("  (none)");
        }
        else
        {
            foreach (string line in lines)
            {
                message.AppendLine($"  {(string.Equals(heading, "Added", StringComparison.Ordinal) ? "+" : "-")} {line}");
            }
        }

        message.AppendLine();
    }
}

/// <summary>
/// A baseline signature and the current signature that replaced it.
/// </summary>
internal sealed class PublicApiChange
{
    public PublicApiChange(string baseline, string current)
    {
        Baseline = baseline;
        Current = current;
    }

    /// <summary>
    /// Gets the checked-in baseline signature.
    /// </summary>
    public string Baseline { get; }

    /// <summary>
    /// Gets the current compiled signature.
    /// </summary>
    public string Current { get; }
}

/// <summary>
/// Compares two named sets and reports new and stale entries in both directions.
/// </summary>
internal static class SetComparison
{
    /// <summary>
    /// Compares an expected set with an actual set.
    /// </summary>
    /// <param name="expected">The expected entries.</param>
    /// <param name="actual">The actual entries.</param>
    /// <returns>The new and stale entries.</returns>
    public static (IReadOnlyList<string> NewEntries, IReadOnlyList<string> StaleEntries) Compare(
        IEnumerable<string> expected,
        IEnumerable<string> actual)
    {
        var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
        var actualSet = new HashSet<string>(actual, StringComparer.Ordinal);

        return (
            actualSet.Except(expectedSet, StringComparer.Ordinal).OrderBy(entry => entry, StringComparer.Ordinal).ToArray(),
            expectedSet.Except(actualSet, StringComparer.Ordinal).OrderBy(entry => entry, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Renders a reviewable report for a set comparison.
    /// </summary>
    /// <param name="name">The name of the compared set.</param>
    /// <param name="guidance">Guidance shown before the entries.</param>
    /// <param name="newEntries">The entries present only in the actual set.</param>
    /// <param name="staleEntries">The entries present only in the expected set.</param>
    /// <returns>The report text.</returns>
    public static string Describe(
        string name,
        string guidance,
        IReadOnlyList<string> newEntries,
        IReadOnlyList<string> staleEntries)
    {
        var message = new StringBuilder()
            .AppendLine($"{name} has changed.")
            .AppendLine(guidance)
            .AppendLine();

        Append(message, "New entries", newEntries, "+");
        Append(message, "Stale entries", staleEntries, "-");

        return message.ToString();
    }

    private static void Append(StringBuilder message, string heading, IReadOnlyList<string> entries, string marker)
    {
        message.AppendLine($"{heading} ({entries.Count}):");

        if (entries.Count == 0)
        {
            message.AppendLine("  (none)");
        }
        else
        {
            foreach (string entry in entries)
            {
                message.AppendLine($"  {marker} {entry}");
            }
        }

        message.AppendLine();
    }
}
