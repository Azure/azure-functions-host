// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.PublicApi.Tests;

/// <summary>
/// Enforces the six audited compiled records that Azure Functions Core Tools compiles against.
/// </summary>
/// <remarks>
/// Unlike the rest of the compiled baseline, these records may not be blessed away by the baseline
/// update workflow. They require a coordinated Core Tools change first.
/// </remarks>
public class CoreToolsCompatibilityTests
{
    private static readonly string[] ExpectedRecordIds =
    {
        "ExtensionBundleConfigurationHelper.ctor",
        "ExtensionBundleManager.ctor",
        "FeatureFlags.IsEnabled",
        "IEnvironment",
        "SystemEnvironment",
        "SystemEnvironment.Instance"
    };

    [Fact]
    public void ContractPreservesExactlyTheSixAuditedRecords()
    {
        CoreToolsCompatibilityContract contract = CoreToolsCompatibilityContract.Load();

        Assert.Equal(1, contract.FormatVersion);
        Assert.Equal(6, contract.Preserve.Length);
        Assert.Equal(
            ExpectedRecordIds,
            contract.Preserve.Select(record => record.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());

        Assert.All(contract.Preserve, record =>
        {
            Assert.False(string.IsNullOrWhiteSpace(record.Assembly));
            Assert.False(string.IsNullOrWhiteSpace(record.Kind));
            Assert.False(string.IsNullOrWhiteSpace(record.Identity));
            Assert.False(string.IsNullOrWhiteSpace(record.Signature));
            Assert.Equal("public", record.EffectiveAccessibility);
            Assert.StartsWith(record.EffectiveAccessibility, record.Signature, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void PreservedRecordsRemainInTheCompiledSnapshot()
    {
        CoreToolsCompatibilityContract contract = CoreToolsCompatibilityContract.Load();
        var failures = new StringBuilder();

        foreach (CoreToolsCompatibilityContract.PreservedRecord record in contract.Preserve)
        {
            PublicApiSnapshot snapshot = CompiledPublicApi.GetSnapshot(record.Assembly);

            PublicApiRecord[] matches = snapshot.Records
                .Where(candidate => string.Equals(candidate.Kind, record.Kind, StringComparison.Ordinal)
                    && string.Equals(candidate.Identity, record.Identity, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length == 0)
            {
                failures
                    .AppendLine($"Core Tools required record '{record.Id}' is missing from '{record.Assembly}'.")
                    .AppendLine($"  expected: {record.ToBaselineLine()}")
                    .AppendLine();
                continue;
            }

            PublicApiRecord match = Assert.Single(matches);
            if (!string.Equals(match.Signature, record.Signature, StringComparison.Ordinal))
            {
                failures
                    .AppendLine($"Core Tools required record '{record.Id}' changed in '{record.Assembly}'.")
                    .AppendLine($"  expected: {record.Signature}")
                    .AppendLine($"  actual:   {match.Signature}")
                    .AppendLine();
            }
        }

        Assert.True(
            failures.Length == 0,
            new StringBuilder()
                .AppendLine("The audited Core Tools compatibility contract is broken.")
                .AppendLine("Azure Functions Core Tools 'main' compiles against these records. Restore them, or change the")
                .AppendLine($"contract and its evidence in '{CoreToolsCompatibilityContract.RelativePath}' as part of a coordinated Core Tools change.")
                .AppendLine()
                .Append(failures)
                .ToString());
    }

    [Fact]
    public void PreservedRecordsRetainPublicEffectiveAccessibility()
    {
        CoreToolsCompatibilityContract contract = CoreToolsCompatibilityContract.Load();

        foreach (CoreToolsCompatibilityContract.PreservedRecord record in contract.Preserve)
        {
            PublicApiSnapshot snapshot = CompiledPublicApi.GetSnapshot(record.Assembly);

            PublicApiRecord match = snapshot.Records.SingleOrDefault(candidate =>
                string.Equals(candidate.Kind, record.Kind, StringComparison.Ordinal)
                && string.Equals(candidate.Identity, record.Identity, StringComparison.Ordinal));

            Assert.True(match is not null, $"Core Tools required record '{record.Id}' is missing from '{record.Assembly}'.");
            Assert.StartsWith(
                record.EffectiveAccessibility + " ",
                match.Signature,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PreservedRecordsAreAlsoRecordedInTheCheckedInBaselines()
    {
        CoreToolsCompatibilityContract contract = CoreToolsCompatibilityContract.Load();
        ShippedAssemblyManifest manifest = ShippedAssemblyManifest.Load();

        foreach (IGrouping<string, CoreToolsCompatibilityContract.PreservedRecord> group in contract.Preserve
            .GroupBy(record => record.Assembly, StringComparer.Ordinal))
        {
            ShippedAssemblyManifest.ShippedAssembly assembly = manifest.Assemblies
                .Single(candidate => string.Equals(candidate.BaselineAssemblyName, group.Key, StringComparison.Ordinal));

            IReadOnlyList<string> baseline = PublicApiSnapshotBuilder.ReadLines(File.ReadAllText(assembly.GetBaselinePath()));

            foreach (CoreToolsCompatibilityContract.PreservedRecord record in group)
            {
                Assert.Contains(record.ToBaselineLine(), baseline);
            }
        }
    }

    [Fact]
    public void AuditRecordsBothSupportedBranchesAndEveryCallSite()
    {
        CoreToolsCompatibilityContract contract = CoreToolsCompatibilityContract.Load();

        Assert.Equal("Azure/azure-functions-core-tools", contract.Audit.Repository);
        Assert.Equal(2, contract.Audit.Branches.Length);
        Assert.Equal(
            new[] { "main", "vnext" },
            contract.Audit.Branches.Select(branch => branch.Branch).OrderBy(branch => branch, StringComparer.Ordinal).ToArray());

        Assert.All(contract.Audit.Branches, branch =>
        {
            Assert.Matches("^[0-9a-f]{40}$", branch.Commit);
            Assert.Equal("Microsoft.Azure.WebJobs.Script.WebHost", branch.HostPackage);
            Assert.False(string.IsNullOrWhiteSpace(branch.HostPackageVersion));
            Assert.False(string.IsNullOrWhiteSpace(branch.Notes));
        });

        CoreToolsCompatibilityContract.AuditedBranch main = contract.Audit.Branches.Single(branch => string.Equals(branch.Branch, "main", StringComparison.Ordinal));
        CoreToolsCompatibilityContract.AuditedBranch next = contract.Audit.Branches.Single(branch => string.Equals(branch.Branch, "vnext", StringComparison.Ordinal));

        Assert.True(main.ConsumesMigrationApi);
        Assert.False(next.ConsumesMigrationApi);

        Assert.Equal(3, contract.Audit.CallSites.Length);
        Assert.All(contract.Audit.CallSites, callSite =>
        {
            Assert.Equal("main", callSite.Branch);
            Assert.False(string.IsNullOrWhiteSpace(callSite.File));
            Assert.True(callSite.Line > 0);
            Assert.Equal("SystemEnvironment.Instance", callSite.Argument);
            Assert.NotEmpty(callSite.PreserveRecordIds);
        });

        var preservedIds = new HashSet<string>(contract.Preserve.Select(record => record.Id), StringComparer.Ordinal);
        Assert.All(
            contract.Audit.CallSites.SelectMany(callSite => callSite.PreserveRecordIds),
            id => Assert.Contains(id, preservedIds));

        Assert.Equal(
            preservedIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            contract.Audit.CallSites
                .SelectMany(callSite => callSite.PreserveRecordIds)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray());

        Assert.NotEmpty(contract.Audit.NotPreserved.Members);
        Assert.False(string.IsNullOrWhiteSpace(contract.Audit.NotPreserved.Reason));
    }

    [Fact]
    public void MembersOutsideThePreserveSetAreNotProtected()
    {
        CoreToolsCompatibilityContract contract = CoreToolsCompatibilityContract.Load();
        PublicApiSnapshot snapshot = CompiledPublicApi.GetSnapshot("Microsoft.Azure.WebJobs.Script");

        var preservedIdentities = new HashSet<string>(contract.Preserve.Select(record => record.Identity), StringComparer.Ordinal);

        foreach (string member in contract.Audit.NotPreserved.Members)
        {
            Assert.DoesNotContain(member, preservedIdentities);
            Assert.Contains(snapshot.Records, record => string.Equals(record.Identity, member, StringComparison.Ordinal));
        }
    }
}
