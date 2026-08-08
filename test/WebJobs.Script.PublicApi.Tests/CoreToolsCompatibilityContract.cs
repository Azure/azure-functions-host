// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script.PublicApi.Tests;

/// <summary>
/// The audited, hard compatibility contract with Azure Functions Core Tools.
/// </summary>
internal sealed class CoreToolsCompatibilityContract
{
    /// <summary>
    /// The repository-relative path of the contract file.
    /// </summary>
    public const string RelativePath = "test/WebJobs.Script.PublicApi.Tests/CoreToolsCompatibilityContract.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Gets or sets the contract format version.
    /// </summary>
    public int FormatVersion { get; set; }

    /// <summary>
    /// Gets or sets the human-readable description of the contract.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the read-only Core Tools consumer audit.
    /// </summary>
    public CoreToolsAudit Audit { get; set; } = new();

    /// <summary>
    /// Gets or sets the compiled records that must be preserved.
    /// </summary>
    public PreservedRecord[] Preserve { get; set; } = Array.Empty<PreservedRecord>();

    /// <summary>
    /// Gets or sets the feature-owned requirements that must be satisfied before preserved records are removed.
    /// </summary>
    public RemovalRequirement[] RemovalRequirements { get; set; } = Array.Empty<RemovalRequirement>();

    /// <summary>
    /// Gets or sets the human-reviewed policy for supported Core Tools integration paths.
    /// </summary>
    public ProtectedIntegrationPath[] ProtectedIntegrationPaths { get; set; } = Array.Empty<ProtectedIntegrationPath>();

    /// <summary>
    /// Loads the contract from the repository.
    /// </summary>
    /// <returns>The contract.</returns>
    public static CoreToolsCompatibilityContract Load()
    {
        string path = RepositoryPaths.Combine(RelativePath);
        CoreToolsCompatibilityContract contract = JsonSerializer.Deserialize<CoreToolsCompatibilityContract>(File.ReadAllText(path), SerializerOptions);

        return contract ?? throw new InvalidOperationException($"Unable to read the Core Tools compatibility contract '{path}'.");
    }

    /// <summary>
    /// The audited Core Tools branches and call sites.
    /// </summary>
    internal sealed class CoreToolsAudit
    {
        /// <summary>
        /// Gets or sets the audit scope description.
        /// </summary>
        public string Scope { get; set; }

        /// <summary>
        /// Gets or sets the audited repository.
        /// </summary>
        public string Repository { get; set; }

        /// <summary>
        /// Gets or sets the audited branches.
        /// </summary>
        public AuditedBranch[] Branches { get; set; } = Array.Empty<AuditedBranch>();

        /// <summary>
        /// Gets or sets the audited compile-time call sites.
        /// </summary>
        public AuditedCallSite[] CallSites { get; set; } = Array.Empty<AuditedCallSite>();

        /// <summary>
        /// Gets or sets the migration members that are deliberately not preserved.
        /// </summary>
        public NotPreserved NotPreserved { get; set; } = new();
    }

    /// <summary>
    /// An audited Core Tools branch.
    /// </summary>
    internal sealed class AuditedBranch
    {
        /// <summary>
        /// Gets or sets the branch name.
        /// </summary>
        public string Branch { get; set; }

        /// <summary>
        /// Gets or sets the audited commit SHA.
        /// </summary>
        public string Commit { get; set; }

        /// <summary>
        /// Gets or sets the pinned host package identifier.
        /// </summary>
        public string HostPackage { get; set; }

        /// <summary>
        /// Gets or sets the pinned host package version.
        /// </summary>
        public string HostPackageVersion { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the branch consumes migration-related API.
        /// </summary>
        public bool ConsumesMigrationApi { get; set; }

        /// <summary>
        /// Gets or sets the audit notes.
        /// </summary>
        public string Notes { get; set; }

        /// <summary>
        /// Gets or sets whether the evidence is current or historical.
        /// </summary>
        public string EvidenceStatus { get; set; }
    }

    /// <summary>
    /// An audited compile-time Core Tools call site.
    /// </summary>
    internal sealed class AuditedCallSite
    {
        /// <summary>
        /// Gets or sets the branch the call site belongs to.
        /// </summary>
        public string Branch { get; set; }

        /// <summary>
        /// Gets or sets the Core Tools repository-relative source file.
        /// </summary>
        public string File { get; set; }

        /// <summary>
        /// Gets or sets the source line number.
        /// </summary>
        public int Line { get; set; }

        /// <summary>
        /// Gets or sets the invoked host entry point.
        /// </summary>
        public string Call { get; set; }

        /// <summary>
        /// Gets or sets the argument supplied for the <c>IEnvironment</c> parameter.
        /// </summary>
        public string Argument { get; set; }

        /// <summary>
        /// Gets or sets the preserved record identifiers the call site depends on.
        /// </summary>
        public string[] PreserveRecordIds { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Migration members that are intentionally excluded from the hard preserve set.
    /// </summary>
    internal sealed class NotPreserved
    {
        /// <summary>
        /// Gets or sets the excluded member identities.
        /// </summary>
        public string[] Members { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the reason the members are excluded.
        /// </summary>
        public string Reason { get; set; }
    }

    /// <summary>
    /// A compiled record that must be preserved for Core Tools.
    /// </summary>
    internal sealed class PreservedRecord
    {
        /// <summary>
        /// Gets or sets the stable record identifier used by call sites and the classification ledger.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the shipped assembly that declares the record.
        /// </summary>
        public string Assembly { get; set; }

        /// <summary>
        /// Gets or sets the record kind.
        /// </summary>
        public string Kind { get; set; }

        /// <summary>
        /// Gets or sets the unique compiled record identity.
        /// </summary>
        public string Identity { get; set; }

        /// <summary>
        /// Gets or sets the canonical compiled signature.
        /// </summary>
        public string Signature { get; set; }

        /// <summary>
        /// Gets or sets the required effective accessibility.
        /// </summary>
        public string EffectiveAccessibility { get; set; }

        /// <summary>
        /// Gets or sets the feature-owned removal requirement for this record.
        /// </summary>
        public string RemovalRequirementId { get; set; }

        /// <summary>
        /// Gets the rendered baseline line the record must match.
        /// </summary>
        /// <returns>The baseline line.</returns>
        public string ToBaselineLine()
        {
            return $"{Kind} | {Identity} | {Signature}";
        }
    }

    /// <summary>
    /// A mechanical replacement and removal gate for one or more preserved records.
    /// </summary>
    internal sealed class RemovalRequirement
    {
        /// <summary>
        /// Gets or sets the stable requirement identifier.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the records governed by this requirement.
        /// </summary>
        public string[] RecordIds { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the feature-owned replacement or retirement action.
        /// </summary>
        public string Replacement { get; set; }

        /// <summary>
        /// Gets or sets the mandatory gates that must all pass before removal.
        /// </summary>
        public string[] Gates { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets prerequisite removal requirements.
        /// </summary>
        public string[] DependsOnRequirementIds { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Human-reviewed policy for a supported feature-owned integration path outside the six-record hard gate.
    /// </summary>
    internal sealed class ProtectedIntegrationPath
    {
        /// <summary>
        /// Gets or sets the stable path identifier.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the integration path description.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Gets or sets the compatibility treatment.
        /// </summary>
        public string Treatment { get; set; }
    }
}
