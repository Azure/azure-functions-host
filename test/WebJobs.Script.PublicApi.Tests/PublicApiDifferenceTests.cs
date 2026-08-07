// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.PublicApi.Tests;

/// <summary>
/// Proves the gate reports added, removed, and changed signatures correctly, that the formatter
/// records every compatibility-significant detail, and that rendering is order independent.
/// </summary>
public class PublicApiDifferenceTests
{
    private const string ConstructorLine =
        "constructor | Contoso.Widget..ctor(System.String) | public Widget(System.String name)";

    private const string TypeLine =
        "type | Contoso.Widget | public class Contoso.Widget";

    [Fact]
    public void RemovingAConstructorIsReportedAsRemoved()
    {
        PublicApiDifference difference = PublicApiDifference.Compare(
            new[] { TypeLine, ConstructorLine },
            new[] { TypeLine });

        Assert.Equal(new[] { ConstructorLine }, difference.Removed);
        Assert.Empty(difference.Added);
        Assert.Empty(difference.Changed);
        Assert.False(difference.IsEmpty);
    }

    [Fact]
    public void ChangingAParameterTypeIsReportedAsChanged()
    {
        const string updated = "constructor | Contoso.Widget..ctor(System.Int32) | public Widget(System.Int32 id)";

        PublicApiDifference difference = PublicApiDifference.Compare(
            new[] { TypeLine, ConstructorLine },
            new[] { TypeLine, updated });

        PublicApiChange change = Assert.Single(difference.Changed);
        Assert.Equal(ConstructorLine, change.Baseline);
        Assert.Equal(updated, change.Current);
        Assert.Empty(difference.Added);
        Assert.Empty(difference.Removed);
    }

    [Fact]
    public void ChangingARefModifierOrDefaultValueIsReportedAsChanged()
    {
        const string baselineRef = "method | Contoso.Widget.TryGet(System.String) | public System.Boolean TryGet(System.String value)";
        const string currentRef = "method | Contoso.Widget.TryGet(System.String&) | public System.Boolean TryGet(out System.String value)";
        const string baselineDefault = "method | Contoso.Widget.Build(System.Int32) | public System.Void Build(System.Int32 retries = 3)";
        const string currentDefault = "method | Contoso.Widget.Build(System.Int32) | public System.Void Build(System.Int32 retries = 5)";

        PublicApiDifference refDifference = PublicApiDifference.Compare(new[] { baselineRef }, new[] { currentRef });
        PublicApiChange refChange = Assert.Single(refDifference.Changed);
        Assert.Equal(currentRef, refChange.Current);

        PublicApiDifference defaultDifference = PublicApiDifference.Compare(new[] { baselineDefault }, new[] { currentDefault });
        PublicApiChange defaultChange = Assert.Single(defaultDifference.Changed);
        Assert.Equal(currentDefault, defaultChange.Current);
    }

    [Fact]
    public void AddingAPublicTypeOrMemberIsReportedAsAdded()
    {
        const string addedType = "type | Contoso.Gadget | public class Contoso.Gadget";
        const string addedMember = "method | Contoso.Widget.Reset() | public System.Void Reset()";

        PublicApiDifference difference = PublicApiDifference.Compare(
            new[] { TypeLine },
            new[] { TypeLine, addedType, addedMember });

        Assert.Equal(new[] { addedMember, addedType }.OrderBy(line => line, StringComparer.Ordinal).ToArray(), difference.Added);
        Assert.Empty(difference.Removed);
        Assert.Empty(difference.Changed);
    }

    [Fact]
    public void OverloadAdditionIsReportedAsAddedRatherThanChanged()
    {
        const string overload = "constructor | Contoso.Widget..ctor(System.String,System.Int32) | public Widget(System.String name, System.Int32 id)";

        PublicApiDifference difference = PublicApiDifference.Compare(
            new[] { TypeLine, ConstructorLine },
            new[] { TypeLine, ConstructorLine, overload });

        Assert.Equal(new[] { overload }, difference.Added);
        Assert.Empty(difference.Removed);
        Assert.Empty(difference.Changed);
    }

    [Fact]
    public void MatchingBaselineReportsNoDifference()
    {
        PublicApiDifference difference = PublicApiDifference.Compare(
            new[] { TypeLine, ConstructorLine },
            new[] { ConstructorLine, TypeLine });

        Assert.True(difference.IsEmpty);
    }

    [Fact]
    public void RenderingIsIndependentOfReflectionEnumerationOrder()
    {
        PublicApiSnapshot snapshot = PublicApiSnapshotBuilder.Create(typeof(FormatterFixtures).Assembly);

        var shuffled = snapshot.Records.ToList();
        shuffled.Reverse();

        string forward = PublicApiSnapshotBuilder.Render(snapshot.Records);
        string reversed = PublicApiSnapshotBuilder.Render(shuffled);

        Assert.Equal(forward, reversed);
        Assert.DoesNotContain('\r', forward);
        Assert.EndsWith("\n", forward, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedSnapshotsOfTheSameAssemblyAreIdentical()
    {
        Assembly assembly = typeof(FormatterFixtures).Assembly;

        Assert.Equal(
            PublicApiSnapshotBuilder.Create(assembly).Lines,
            PublicApiSnapshotBuilder.Create(assembly).Lines);
    }

    [Theory]
    [InlineData("type | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample | [DefaultMember(\"Item\")] public class Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample : System.IDisposable")]
    [InlineData("constructor | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample..ctor(System.String) | public Sample(System.String! name)")]
    [InlineData("constructor | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample..ctor() | protected Sample()")]
    [InlineData("field | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.DefaultName | public const System.String! DefaultName = \"widget\"")]
    [InlineData("field | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.Ratio | public const System.Decimal Ratio = 1.5m")]
    [InlineData("field | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.Marker | protected readonly System.Char Marker")]
    [InlineData("property | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.Name | public System.String! Name { get; protected set; }")]
    [InlineData("property | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.Tag | public System.String? Tag { get; init; }")]
    [InlineData("property | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.Item[System.Int32] | public System.String! Item[System.Int32 index] { get; }")]
    [InlineData("event | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.Changed | public event System.EventHandler? Changed { add; remove; }")]
    [InlineData("method | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.TryParse(System.String,System.Int32&) | public static System.Boolean TryParse(System.String? text, out System.Int32 value)")]
    [InlineData("method | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.Sum(System.Int32[]) | public static System.Int32 Sum(params System.Int32[]! values)")]
    [InlineData("method | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.Scale(System.Double&) | public static System.Void Scale(in System.Double factor)")]
    [InlineData("method | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.CreateNullable(System.String&) | public static System.Void CreateNullable(out System.String? value)")]
    [InlineData("method | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.Update(System.String&) | public static System.Void Update(ref System.String! value)")]
    [InlineData("method | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.Inspect(System.String&) | public static System.Void Inspect(in System.String? value)")]
    [InlineData("method | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.PeekNullable() | public static ref readonly System.String? PeekNullable()")]
    [InlineData("method | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.Describe(System.String,System.Int32,System.Boolean,System.Char,Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Level) | public static System.String! Describe(System.String? text = \"a\\\\b\\\"c\", System.Int32 count = 7, System.Boolean flag = true, System.Char separator = ';', Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Level level = (Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Level)2)")]
    [InlineData("method | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.Pick`1(System.Collections.Generic.IEnumerable<T>) | public static T Pick<T>(System.Collections.Generic.IEnumerable<T>! source) where T : System.IComparable<T>, new()")]
    [InlineData("method | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.op_Implicit(Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample) | public static System.String! op_Implicit(Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample! value)")]
    [InlineData("method | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.Dispose() | public sealed System.Void Dispose()")]
    [InlineData("method | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample.Render() | protected virtual System.String! Render()")]
    [InlineData("type | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Derived | public sealed class Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Derived : Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Sample")]
    [InlineData("method | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Derived.Render() | protected sealed override System.String! Render()")]
    [InlineData("type | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Level | [Flags] public enum Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Level : System.Byte")]
    [InlineData("field | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Level.High | public const Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Level High = (Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Level)2")]
    [InlineData("type | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Point | public readonly struct Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Point")]
    [InlineData("type | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Span | [Obsolete(\"Types with embedded references are not supported in this version of your compiler.\", true)] public ref struct Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Span")]
    [InlineData("type | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.IPair`2 | public interface Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.IPair<TKey, TValue> where TKey : class where TValue : struct")]
    [InlineData("type | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.IVariant`2 | public interface Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.IVariant<in TIn, out TOut>")]
    [InlineData("type | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Callback | public delegate Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Callback")]
    [InlineData("method | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Callback.Invoke(System.Int32) | public virtual System.Void Invoke(System.Int32 value)")]
    [InlineData("type | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtureHelpers | [Extension] public static class Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtureHelpers")]
    [InlineData("method | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtureHelpers.Twice(System.Int32) | [Extension] public static System.Int32 Twice(this System.Int32 value)")]
    [InlineData("method | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtureHelpers.Legacy() | [EditorBrowsable((System.ComponentModel.EditorBrowsableState)1)] [Obsolete(\"gone\", false)] public static System.Void Legacy()")]
    [InlineData("type | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Money | public record struct Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Money : System.IEquatable<Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Money>")]
    [InlineData("type | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Receipt | public record class Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Receipt : System.IEquatable<Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Receipt>")]
    [InlineData("constructor | Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Receipt..ctor(Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Receipt) | protected Receipt(Microsoft.Azure.WebJobs.Script.PublicApi.Tests.FormatterFixtures.Receipt! original)")]
    public void FormatterRecordsCompatibilitySignificantDetail(string expected)
    {
        PublicApiSnapshot snapshot = PublicApiSnapshotBuilder.Create(typeof(FormatterFixtures).Assembly);

        Assert.Contains(expected, snapshot.Lines);
    }

    [Fact]
    public void FormatterExcludesMembersThatAreNotExternallyVisible()
    {
        PublicApiSnapshot snapshot = PublicApiSnapshotBuilder.Create(typeof(FormatterFixtures).Assembly);
        string[] lines = snapshot.Lines.ToArray();

        Assert.DoesNotContain(lines, line => line.Contains("HiddenType", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains(".Secret", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains(".PrivateProtected", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("get_Name", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("add_Changed", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("value__", StringComparison.Ordinal));
    }

    [Fact]
    public void AssemblyIdentityIsRecordedWithoutPerBuildDetail()
    {
        PublicApiSnapshot snapshot = PublicApiSnapshotBuilder.Create(typeof(FormatterFixtures).Assembly);
        string[] identity = snapshot.Records
            .Where(record => string.Equals(record.Kind, "assembly", StringComparison.Ordinal))
            .Select(record => record.Identity)
            .ToArray();

        Assert.Equal(
            new[] { "culture", "name", "publicKeyToken", "targetFramework", "version" },
            identity.OrderBy(value => value, StringComparer.Ordinal).ToArray());

        Assert.DoesNotContain(snapshot.Lines, line => line.Contains("42.42.42.4242", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestCompletenessFailsForAMissingOrExtraPackageEntry()
    {
        ShippedAssemblyManifest manifest = ShippedAssemblyManifest.Load();
        string[] expected = manifest.Packages.Select(package => "**/" + System.IO.Path.GetFileName(package.PackageProject)).ToArray();

        (IReadOnlyList<string> missingNew, IReadOnlyList<string> missingStale) = SetComparison.Compare(
            expected,
            expected.Take(expected.Length - 1));

        Assert.Empty(missingNew);
        Assert.Single(missingStale);

        (IReadOnlyList<string> extraNew, IReadOnlyList<string> extraStale) = SetComparison.Compare(
            expected,
            expected.Append("**/Contoso.NewlyPacked.csproj"));

        Assert.Equal(new[] { "**/Contoso.NewlyPacked.csproj" }, extraNew);
        Assert.Empty(extraStale);
    }

    [Fact]
    public void PackJobReaderReadsOnlyPackStepProjects()
    {
        string[] template =
        {
            "steps:",
            "- task: DotNetCoreCLI@2",
            "  displayName: Build only",
            "  inputs:",
            "    command: build",
            "    projects: |",
            "      **/NotPacked.csproj",
            string.Empty,
            "- task: DotNetCoreCLI@2",
            "  displayName: Pack",
            "  inputs:",
            "    command: custom",
            "    custom: pack",
            "    arguments: -c release",
            "    projects: |",
            "      **/First.csproj",
            "      **/Second.csproj",
            string.Empty,
            "- task: DotNetCoreCLI@2",
            "  displayName: Pack single",
            "  inputs:",
            "    command: custom",
            "    custom: pack",
            "    projects: '**/Third.csproj'"
        };

        Assert.Equal(
            new[] { "**/First.csproj", "**/Second.csproj", "'**/Third.csproj'" },
            PackJobReader.ReadPackedProjects(template));
    }
}
