// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.StaticAnalysis;

public class EnvironmentMigrationSourceUsageTests
{
    private const string UpdateBaselinesEnvironmentVariable = "UPDATE_ENVIRONMENT_MIGRATION_BASELINES";
    private const string InventoryRelativePath = "test/WebJobs.Script.Tests/StaticAnalysis/EnvironmentMigration/EnvironmentMigrationInventory.json";
    private const string AllowlistRelativePath = "test/WebJobs.Script.Tests/StaticAnalysis/EnvironmentMigration/EnvironmentMigrationAllowlist.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    [Fact]
    public void BaselinesMatchCurrentSource()
    {
        string repositoryRoot = FindRepositoryRoot();
        EnvironmentMigrationSnapshot snapshot = new EnvironmentMigrationSourceScanner().ScanRepository(repositoryRoot);
        EnvironmentMigrationInventory actualInventory = CreateInventory(snapshot);
        EnvironmentMigrationAllowlist actualAllowlist = CreateAllowlist(snapshot);

        string inventoryPath = Path.Combine(repositoryRoot, InventoryRelativePath);
        string allowlistPath = Path.Combine(repositoryRoot, AllowlistRelativePath);

        if (string.Equals(
            Environment.GetEnvironmentVariable(UpdateBaselinesEnvironmentVariable),
            "1",
            StringComparison.Ordinal))
        {
            WriteBaseline(inventoryPath, actualInventory);
            WriteBaseline(allowlistPath, actualAllowlist);
            return;
        }

        EnvironmentMigrationInventory expectedInventory = ReadBaseline<EnvironmentMigrationInventory>(inventoryPath);
        EnvironmentMigrationAllowlist expectedAllowlist = ReadBaseline<EnvironmentMigrationAllowlist>(allowlistPath);

        Assert.Equal(expectedInventory.FormatVersion, actualInventory.FormatVersion);
        AssertEntriesEqual(expectedInventory.EnvironmentExtensionHelpers, actualInventory.EnvironmentExtensionHelpers, nameof(EnvironmentMigrationInventory.EnvironmentExtensionHelpers));
        AssertEntriesEqual(expectedInventory.DirectEnvironmentReads, actualInventory.DirectEnvironmentReads, nameof(EnvironmentMigrationInventory.DirectEnvironmentReads));
        AssertEntriesEqual(expectedInventory.DirectEnvironmentWrites, actualInventory.DirectEnvironmentWrites, nameof(EnvironmentMigrationInventory.DirectEnvironmentWrites));
        AssertEntriesEqual(expectedInventory.StaticAccesses, actualInventory.StaticAccesses, nameof(EnvironmentMigrationInventory.StaticAccesses));
        AssertEntriesEqual(expectedInventory.EnvironmentPredicates, actualInventory.EnvironmentPredicates, nameof(EnvironmentMigrationInventory.EnvironmentPredicates));
        AssertEntriesEqual(expectedInventory.PublicSignatures, actualInventory.PublicSignatures, nameof(EnvironmentMigrationInventory.PublicSignatures));
        AssertEntriesEqual(expectedInventory.TestSeams, actualInventory.TestSeams, nameof(EnvironmentMigrationInventory.TestSeams));

        Assert.Equal(expectedAllowlist.FormatVersion, actualAllowlist.FormatVersion);
        AssertEntriesEqual(expectedAllowlist.IEnvironment, actualAllowlist.IEnvironment, nameof(EnvironmentMigrationAllowlist.IEnvironment));
        AssertEntriesEqual(expectedAllowlist.SystemEnvironmentInstance, actualAllowlist.SystemEnvironmentInstance, nameof(EnvironmentMigrationAllowlist.SystemEnvironmentInstance));
        AssertEntriesEqual(expectedAllowlist.ScriptSettingsManagerInstance, actualAllowlist.ScriptSettingsManagerInstance, nameof(EnvironmentMigrationAllowlist.ScriptSettingsManagerInstance));
        AssertEntriesEqual(expectedAllowlist.EnvironmentPredicates, actualAllowlist.EnvironmentPredicates, nameof(EnvironmentMigrationAllowlist.EnvironmentPredicates));
    }

    [Fact]
    public void UsageKeysIgnoreFormattingAndLineMovement()
    {
        const string compactSource = @"
class Example
{
    void Run(IEnvironment environment)
    {
        environment.IsFlexConsumptionSku();
    }
}";

        const string movedSource = @"


class Example
{
    void Run(
        IEnvironment environment)
    {
        // Moving or documenting an existing use must not change its key.
#if DEBUG
        environment
            .IsFlexConsumptionSku(
            );
#endif
    }
}";

        EnvironmentMigrationSnapshot compact = ScanSynthetic(compactSource);
        EnvironmentMigrationSnapshot moved = ScanSynthetic(movedSource);

        Assert.Equal(ToKeys(compact.IEnvironmentUsages), ToKeys(moved.IEnvironmentUsages));
        Assert.Equal(ToKeys(compact.EnvironmentPredicateUsages), ToKeys(moved.EnvironmentPredicateUsages));
    }

    [Fact]
    public void UsageKeysIgnoreLineEndingDifferences()
    {
        const string source = "class Example\n{\n    void Run(IEnvironment environment)\n    {\n        environment.IsFlexConsumptionSku();\n    }\n}";
        string sourceWithCrLf = source.Replace("\n", "\r\n", StringComparison.Ordinal);

        EnvironmentMigrationSnapshot lf = ScanSynthetic(source);
        EnvironmentMigrationSnapshot crlf = ScanSynthetic(sourceWithCrLf);

        Assert.Equal(ToKeys(lf.IEnvironmentUsages), ToKeys(crlf.IEnvironmentUsages));
        Assert.Equal(ToKeys(lf.EnvironmentPredicateUsages), ToKeys(crlf.EnvironmentPredicateUsages));
    }

    [Fact]
    public void ScannerFindsUsagesAfterUnsupportedParserSyntax()
    {
        const string source = @"
record ExampleRecord(int Value);

class Example
{
    void Run(object value)
    {
        object[] values = [];
        if (value is not null)
        {
            _ = SystemEnvironment.Instance;
            _ = value.IsNewFlexEnvironment();
        }
    }
}";

        EnvironmentMigrationAllowlist actual = CreateAllowlist(ScanSynthetic(source));

        Assert.Single(actual.SystemEnvironmentInstance);
        Assert.Single(actual.EnvironmentPredicates);
    }

    [Fact]
    public void RatchetDetectsSyntheticNewForbiddenUsages()
    {
        const string source = @"
class Example
{
    void Run(IEnvironment environment)
    {
        _ = SystemEnvironment.Instance;
        _ = ScriptSettingsManager.Instance;
        _ = environment.IsNewFlexEnvironment();
    }
}";

        EnvironmentMigrationAllowlist actual = CreateAllowlist(ScanSynthetic(source));

        Assert.Single(actual.IEnvironment);
        Assert.Single(actual.SystemEnvironmentInstance);
        Assert.Single(actual.ScriptSettingsManagerInstance);
        Assert.Single(actual.EnvironmentPredicates);
    }

    [Fact]
    public void ApprovedBoundaryTracksButDoesNotRatchetPredicateUsage()
    {
        const string source = @"
namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    class Program
    {
        object CreateHostBuilder(string[] args)
        {
            _ = SystemEnvironment.Instance.IsAppService();
            return null;
        }
    }
}";

        EnvironmentMigrationSnapshot snapshot = new EnvironmentMigrationSourceScanner().Scan(new[]
        {
            new EnvironmentMigrationSourceFile("src/WebJobs.Script.WebHost/Program.cs", source)
        });

        Assert.Single(snapshot.EnvironmentPredicateUsages);
        Assert.Single(CreateInventory(snapshot).EnvironmentPredicates);
        Assert.Empty(CreateAllowlist(snapshot).EnvironmentPredicates);
    }

    [Fact]
    public void ScannerIncludesAllConditionalBranches()
    {
        const string source = @"
class Example
{
    void Run()
    {
#if DEBUG
        _ = SystemEnvironment.Instance;
#else
        _ = ScriptSettingsManager.Instance;
#endif
    }
}";

        EnvironmentMigrationAllowlist actual = CreateAllowlist(ScanSynthetic(source));

        Assert.Single(actual.SystemEnvironmentInstance);
        Assert.Single(actual.ScriptSettingsManagerInstance);
    }

    [Fact]
    public void RatchetTreatsRemovedAllowanceAsStale()
    {
        const string source = @"
class Example
{
    void Run(IEnvironment environment)
    {
    }
}";

        string[] previousAllowlist = ToKeys(ScanSynthetic(source).IEnvironmentUsages);
        string[] currentUsages = Array.Empty<string>();

        (string[] newEntries, string[] staleEntries) = GetBaselineDifferences(
            previousAllowlist,
            currentUsages);

        Assert.Empty(newEntries);
        Assert.Single(staleEntries);
    }

    [Fact]
    public void ScannerIgnoresCommentsAndStringLiterals()
    {
        const string source = @"
class Example
{
    // IEnvironment SystemEnvironment.Instance environment.IsFlexConsumptionSku()
    const string Text = ""IEnvironment SystemEnvironment.Instance ScriptSettingsManager.Instance"";
}";

        EnvironmentMigrationAllowlist actual = CreateAllowlist(ScanSynthetic(source));

        Assert.Empty(actual.IEnvironment);
        Assert.Empty(actual.SystemEnvironmentInstance);
        Assert.Empty(actual.ScriptSettingsManagerInstance);
        Assert.Empty(actual.EnvironmentPredicates);
    }

    [Fact]
    public void ScannerDoesNotTreatMemberNamesAsIEnvironmentTypeReferences()
    {
        const string source = @"
class Baseline
{
    public string IEnvironment { get; set; }

    void Update(Baseline expected, Baseline actual)
    {
        actual.IEnvironment = expected.IEnvironment;
    }
}";

        EnvironmentMigrationAllowlist actual = CreateAllowlist(ScanSynthetic(source));

        Assert.Empty(actual.IEnvironment);
    }

    [Fact]
    public void ScannerIgnoresRawStringLiteralContents()
    {
        string delimiter = new('"', 3);
        string source = $@"
class Example
{{
    string Text = {delimiter}
        IEnvironment
        SystemEnvironment.Instance
        ScriptSettingsManager.Instance
        IsNewFlexEnvironment()
        {delimiter};

    void Run(IEnvironment environment)
    {{
    }}
}}";

        EnvironmentMigrationAllowlist actual = CreateAllowlist(ScanSynthetic(source));

        Assert.Single(actual.IEnvironment);
        Assert.Empty(actual.SystemEnvironmentInstance);
        Assert.Empty(actual.ScriptSettingsManagerInstance);
        Assert.Empty(actual.EnvironmentPredicates);
    }

    [Fact]
    public void ScannerIncludesCodeFromRawStringInterpolations()
    {
        string delimiter = "$" + new string('"', 3);
        string source = $@"
class Example
{{
    string Text = {delimiter}
        literal SystemEnvironment.Instance
        {{SystemEnvironment.Instance}}
        {delimiter};
}}";

        EnvironmentMigrationAllowlist actual = CreateAllowlist(ScanSynthetic(source));

        Assert.Single(actual.SystemEnvironmentInstance);
    }

    [Fact]
    public void InventoryCapturesDirectAccessAndTestSeams()
    {
        const string productionSource = @"
public class Example
{
    public Example(IEnvironment environment)
    {
        Environment.GetEnvironmentVariable(""A"");
        Environment.SetEnvironmentVariable(""A"", ""B"");
    }
}";

        const string testSource = @"
public class CustomEnvironment : IEnvironment
{
    private readonly TestEnvironment _inner = new TestEnvironment();
}";

        var scanner = new EnvironmentMigrationSourceScanner();
        EnvironmentMigrationSnapshot snapshot = scanner.Scan(new[]
        {
            new EnvironmentMigrationSourceFile("src/Example.cs", productionSource),
            new EnvironmentMigrationSourceFile("test/ExampleTests.cs", testSource)
        });

        Assert.Single(snapshot.DirectEnvironmentReads);
        Assert.Single(snapshot.DirectEnvironmentWrites);
        Assert.NotEmpty(snapshot.TestSeams);
    }

    private static EnvironmentMigrationSnapshot ScanSynthetic(string source)
    {
        return new EnvironmentMigrationSourceScanner().Scan(new[]
        {
            new EnvironmentMigrationSourceFile("src/SyntheticEnvironmentMigrationUsage.cs", source)
        });
    }

    private static EnvironmentMigrationInventory CreateInventory(EnvironmentMigrationSnapshot snapshot)
    {
        return new EnvironmentMigrationInventory
        {
            EnvironmentExtensionHelpers = snapshot.EnvironmentExtensionHelpers
                .OrderBy(signature => signature, StringComparer.Ordinal)
                .ToArray(),
            DirectEnvironmentReads = ToKeys(snapshot.DirectEnvironmentReads),
            DirectEnvironmentWrites = ToKeys(snapshot.DirectEnvironmentWrites),
            StaticAccesses = ToKeys(snapshot.SystemEnvironmentInstanceUsages
                .Concat(snapshot.ScriptSettingsManagerInstanceUsages)),
            EnvironmentPredicates = ToKeys(snapshot.EnvironmentPredicateUsages),
            PublicSignatures = snapshot.PublicSignatures
                .OrderBy(signature => signature, StringComparer.Ordinal)
                .ToArray(),
            TestSeams = ToKeys(snapshot.TestSeams)
        };
    }

    private static EnvironmentMigrationAllowlist CreateAllowlist(EnvironmentMigrationSnapshot snapshot)
    {
        return new EnvironmentMigrationAllowlist
        {
            IEnvironment = ToKeys(snapshot.IEnvironmentUsages),
            SystemEnvironmentInstance = ToKeys(snapshot.SystemEnvironmentInstanceUsages),
            ScriptSettingsManagerInstance = ToKeys(snapshot.ScriptSettingsManagerInstanceUsages),
            EnvironmentPredicates = ToKeys(snapshot.EnvironmentPredicateUsages
                .Where(usage => !EnvironmentMigrationSourceScanner.IsApprovedPredicateBoundary(usage)))
        };
    }

    private static string[] ToKeys(IEnumerable<SourceUsage> usages)
    {
        return usages
            .GroupBy(
                usage => $"{usage.Kind}|{usage.RelativePath}|{usage.Syntax}",
                StringComparer.Ordinal)
            .Select(group => $"{group.Key}|count={group.Count()}")
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AssertEntriesEqual(string[] expected, string[] actual, string inventoryName)
    {
        (string[] newEntries, string[] staleEntries) = GetBaselineDifferences(expected, actual);

        if (newEntries.Length == 0 && staleEntries.Length == 0)
        {
            return;
        }

        var message = new StringBuilder()
            .AppendLine($"{inventoryName} has changed.")
            .AppendLine("New entries must be removed or explicitly reviewed before refreshing the baseline.")
            .AppendLine("Stale entries must be removed from the baseline in the same change so deleted debt cannot return.")
            .AppendLine();

        AppendEntries(message, "New entries", newEntries);
        AppendEntries(message, "Stale entries", staleEntries);

        Assert.True(false, message.ToString());
    }

    private static (string[] NewEntries, string[] StaleEntries) GetBaselineDifferences(
        string[] expected,
        string[] actual)
    {
        string[] newEntries = actual
            .Except(expected, StringComparer.Ordinal)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        string[] staleEntries = expected
            .Except(actual, StringComparer.Ordinal)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        return (newEntries, staleEntries);
    }

    private static void AppendEntries(StringBuilder message, string heading, string[] entries)
    {
        message.AppendLine($"{heading}:");
        if (entries.Length == 0)
        {
            message.AppendLine("  (none)");
            return;
        }

        foreach (string entry in entries)
        {
            message.AppendLine($"  + {entry}");
        }
    }

    private static T ReadBaseline<T>(string path)
    {
        T baseline = JsonSerializer.Deserialize<T>(File.ReadAllText(path), SerializerOptions);
        return baseline ?? throw new InvalidOperationException($"Unable to read environment migration baseline '{path}'.");
    }

    private static void WriteBaseline<T>(string path, T baseline)
    {
        string json = JsonSerializer.Serialize(baseline, SerializerOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        File.WriteAllText(path, json + "\n");
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        return TryFindRepositoryRoot(Path.GetDirectoryName(sourceFilePath))
            ?? TryFindRepositoryRoot(AppContext.BaseDirectory)
            ?? TryFindRepositoryRoot(Directory.GetCurrentDirectory())
            ?? throw new DirectoryNotFoundException("Unable to locate WebJobs.Script.sln.");
    }

    private static string TryFindRepositoryRoot(string startPath)
    {
        if (string.IsNullOrEmpty(startPath))
        {
            return null;
        }

        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WebJobs.Script.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private sealed class EnvironmentMigrationInventory
    {
        public int FormatVersion { get; set; } = 1;

        public string[] EnvironmentExtensionHelpers { get; set; } = Array.Empty<string>();

        public string[] DirectEnvironmentReads { get; set; } = Array.Empty<string>();

        public string[] DirectEnvironmentWrites { get; set; } = Array.Empty<string>();

        public string[] StaticAccesses { get; set; } = Array.Empty<string>();

        public string[] EnvironmentPredicates { get; set; } = Array.Empty<string>();

        public string[] PublicSignatures { get; set; } = Array.Empty<string>();

        public string[] TestSeams { get; set; } = Array.Empty<string>();
    }

    private sealed class EnvironmentMigrationAllowlist
    {
        public int FormatVersion { get; set; } = 1;

        public string[] IEnvironment { get; set; } = Array.Empty<string>();

        public string[] SystemEnvironmentInstance { get; set; } = Array.Empty<string>();

        public string[] ScriptSettingsManagerInstance { get; set; } = Array.Empty<string>();

        public string[] EnvironmentPredicates { get; set; } = Array.Empty<string>();
    }
}
