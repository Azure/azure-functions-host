// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.Azure.WebJobs.Script.Tests.StaticAnalysis;

internal sealed class EnvironmentMigrationSourceScanner
{
    private static readonly HashSet<string> EnvironmentPredicateNames = new(StringComparer.Ordinal)
    {
        "IsAnyKubernetesEnvironment",
        "IsAnyLinuxConsumption",
        "IsAppService",
        "IsAppServiceEnvironment",
        "IsConsumptionOnLegion",
        "IsConsumptionSku",
        "IsCoreTools",
        "IsDynamicSku",
        "IsElasticPremiumSku",
        "IsFlexConsumptionSku",
        "IsHostedWindowsEnvironment",
        "IsKubernetesManagedHosting",
        "IsLinuxAppService",
        "IsLinuxAzureManagedHosting",
        "IsLinuxConsumptionOnAtlas",
        "IsLinuxConsumptionOnLegion",
        "IsManagedAppEnvironment",
        "IsWindowsAzureManagedHosting",
        "IsWindowsConsumption",
        "IsWindowsElasticPremium",
        "WebsiteSkuIsDynamic"
    };

    private static readonly string[] EnvironmentPredicateMarkers =
    {
        "AppService",
        "Atlas",
        "Consumption",
        "Container",
        "ContainerApp",
        "CoreTools",
        "Dedicated",
        "Elastic",
        "Flex",
        "HostingEnvironment",
        "Isolated",
        "Kubernetes",
        "Legion",
        "ManagedEnvironment",
        "ManagedHosting",
        "Premium",
        "Standard",
        "Sku"
    };

    private static readonly HashSet<string> ApprovedPredicateBoundaryUsages = new(StringComparer.Ordinal)
    {
        // Existing pre-DI bootstrap/process initialization boundaries.
        "src/WebJobs.Script.WebHost/Program.cs|SystemEnvironment . Instance . IsAnyLinuxConsumption ( )",
        "src/WebJobs.Script.WebHost/Program.cs|SystemEnvironment . Instance . IsAppService ( )",
        "src/WebJobs.Script.WebHost/Program.cs|SystemEnvironment . Instance . IsFlexConsumptionSku ( )",
        "src/WebJobs.Script.WebHost/Program.cs|SystemEnvironment . Instance . IsLinuxAppService ( )",
        "src/WebJobs.Script.WebHost/Program.cs|SystemEnvironment . Instance . IsLinuxConsumptionOnAtlas ( )",
        "src/WebJobs.Script.WebHost/Program.cs|SystemEnvironment . Instance . IsLinuxConsumptionOnLegion ( )",
        "src/WebJobs.Script/ScriptHostBuilderExtensions.cs|SystemEnvironment . Instance . IsKubernetesManagedHosting ( )",
        "src/WebJobs.Script/ScriptHostBuilderExtensions.cs|environment . IsAnyLinuxConsumption ( )",
        "src/WebJobs.Script/ScriptHostBuilderExtensions.cs|environment . IsCoreTools ( )",
        "src/WebJobs.Script/ScriptHostBuilderExtensions.cs|environment . IsWindowsConsumption ( )",
        "src/WebJobs.Script/ScriptHostBuilderExtensions.cs|environment . IsWindowsElasticPremium ( )"
    };

    private static readonly HashSet<string> ApprovedPredicateParityFiles = new(StringComparer.Ordinal)
    {
        // Existing parity-characterization boundaries.
        "test/WebJobs.Script.Tests/Environment/EnvironmentTests.cs",
        "test/WebJobs.Script.Tests/Extensions/EnvironmentExtensionsTests.cs"
    };

    public EnvironmentMigrationSnapshot ScanRepository(string repositoryRoot)
    {
        IEnumerable<EnvironmentMigrationSourceFile> sourceFiles = new[] { "src", "test" }
            .SelectMany(directory => Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, directory),
                "*.cs",
                SearchOption.AllDirectories))
            .Select(path => new EnvironmentMigrationSourceFile(
                NormalizePath(Path.GetRelativePath(repositoryRoot, path)),
                File.ReadAllText(path)))
            .Where(file => !IsGeneratedPath(file.RelativePath));

        EnvironmentMigrationSnapshot snapshot = Scan(sourceFiles);
        AddCompiledInventories(snapshot);
        return snapshot;
    }

    public EnvironmentMigrationSnapshot Scan(IEnumerable<EnvironmentMigrationSourceFile> sourceFiles)
    {
        var snapshot = new EnvironmentMigrationSnapshot();

        foreach (EnvironmentMigrationSourceFile sourceFile in sourceFiles.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            ScanFile(sourceFile, snapshot);
        }

        return snapshot;
    }

    public static bool IsApprovedPredicateBoundary(SourceUsage usage)
    {
        string relativePath = NormalizePath(usage.RelativePath);
        return ApprovedPredicateParityFiles.Contains(relativePath)
            || ApprovedPredicateBoundaryUsages.Contains($"{relativePath}|{usage.Syntax}");
    }

    private static void ScanFile(EnvironmentMigrationSourceFile sourceFile, EnvironmentMigrationSnapshot snapshot)
    {
        string relativePath = NormalizePath(sourceFile.RelativePath);
        bool isProduction = relativePath.StartsWith("src/", StringComparison.Ordinal);
        bool isTest = relativePath.StartsWith("test/", StringComparison.Ordinal);

        var fileSnapshot = new EnvironmentMigrationSnapshot();
        foreach (SyntaxNode root in ParseAllConditionalBranches(sourceFile))
        {
            ScanRoot(relativePath, root, isProduction, isTest, fileSnapshot);
        }

        snapshot.MergeDistinct(fileSnapshot);
    }

    private static void ScanRoot(
        string relativePath,
        SyntaxNode root,
        bool isProduction,
        bool isTest,
        EnvironmentMigrationSnapshot snapshot)
    {
        SyntaxToken[] tokens = GetCodeTokens(root);
        IReadOnlyDictionary<string, string> aliases = GetTypeAliases(tokens);

        for (int index = 0; index < tokens.Length; index++)
        {
            SyntaxToken token = tokens[index];

            if (IsTypeReference(tokens, index, "IEnvironment"))
            {
                snapshot.IEnvironmentUsages.Add(CreateTokenUsage(
                    relativePath,
                    tokens,
                    index,
                    "IEnvironment"));

                if (isTest && IsInterfaceImplementation(tokens, index))
                {
                    snapshot.TestSeams.Add(CreateTokenUsage(
                        relativePath,
                        tokens,
                        index,
                        "IEnvironmentImplementation"));
                }
            }

            if (isTest && IsTypeReference(tokens, index, "TestEnvironment"))
            {
                snapshot.TestSeams.Add(CreateTokenUsage(
                    relativePath,
                    tokens,
                    index,
                    "TestEnvironmentReference"));
            }

            if (TryGetStaticInstanceType(tokens, index, aliases, out string staticType))
            {
                int expressionEnd = FindExpressionEnd(tokens, index);
                SourceUsage usage = CreateUsage(
                    staticType + ".Instance",
                    relativePath,
                    tokens,
                    index,
                    expressionEnd);

                if (string.Equals(staticType, "SystemEnvironment", StringComparison.Ordinal))
                {
                    snapshot.SystemEnvironmentInstanceUsages.Add(usage);
                }
                else
                {
                    snapshot.ScriptSettingsManagerInstanceUsages.Add(usage);
                }
            }

            if (isProduction
                && token.IsKind(SyntaxKind.DotToken)
                && TryGetInvocationName(tokens, index, out string invocationName, out int invocationEnd))
            {
                int expressionStart = FindMemberChainStart(tokens, index);
                if (string.Equals(invocationName, "GetEnvironmentVariable", StringComparison.Ordinal))
                {
                    snapshot.DirectEnvironmentReads.Add(CreateUsage(
                        "GetEnvironmentVariable",
                        relativePath,
                        tokens,
                        expressionStart,
                        invocationEnd));
                }

                if (string.Equals(invocationName, "SetEnvironmentVariable", StringComparison.Ordinal))
                {
                    snapshot.DirectEnvironmentWrites.Add(CreateUsage(
                        "SetEnvironmentVariable",
                        relativePath,
                        tokens,
                        expressionStart,
                        invocationEnd));
                }
            }

            if (token.IsKind(SyntaxKind.IdentifierToken)
                && IsEnvironmentPredicateName(token.ValueText)
                && IsPredicateUsage(tokens, index))
            {
                int expressionStart = index > 0 && tokens[index - 1].IsKind(SyntaxKind.DotToken)
                    ? FindMemberChainStart(tokens, index - 1)
                    : index;
                int expressionEnd = FindExpressionEnd(tokens, index);
                snapshot.EnvironmentPredicateUsages.Add(CreateUsage(
                    "EnvironmentPredicate",
                    relativePath,
                    tokens,
                    expressionStart,
                    expressionEnd));
            }
        }

        ScanUsingStaticDirectives(relativePath, tokens, snapshot);
    }

    private static SyntaxToken[] GetCodeTokens(SyntaxNode root)
    {
        return root.DescendantTokens(descendIntoTrivia: true)
            .Where(token => !token.IsMissing
                && token.Span.Length > 0
                && !IsDocumentationOrDirectiveToken(token))
            .GroupBy(
                token => $"{token.SpanStart}|{token.RawKind}|{token.Text}",
                StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(token => token.SpanStart)
            .ToArray();
    }

    private static bool IsDocumentationOrDirectiveToken(SyntaxToken token)
    {
        return token.Parent?.AncestorsAndSelf().Any(node => node is DocumentationCommentTriviaSyntax
            || node is DirectiveTriviaSyntax) == true;
    }

    private static bool IsTypeReference(SyntaxToken[] tokens, int index, string typeName)
    {
        SyntaxToken token = tokens[index];
        if (!token.IsKind(SyntaxKind.IdentifierToken)
            || !string.Equals(token.ValueText, typeName, StringComparison.Ordinal))
        {
            return false;
        }

        if (token.Parent is TypeDeclarationSyntax typeDeclaration)
        {
            return typeDeclaration.Identifier.Equals(token);
        }

        if (token.Parent is PropertyDeclarationSyntax property
            && property.Identifier.Equals(token))
        {
            return false;
        }

        if (token.Parent is MemberAccessExpressionSyntax memberAccess
            && ReferenceEquals(memberAccess.Name, token.Parent))
        {
            return false;
        }

        if (index > 0 && tokens[index - 1].IsKind(SyntaxKind.DotToken))
        {
            return false;
        }

        return index + 1 >= tokens.Length
            || (!tokens[index + 1].IsKind(SyntaxKind.EqualsToken)
                && !tokens[index + 1].IsKind(SyntaxKind.ColonToken));
    }

    private static bool IsInterfaceImplementation(SyntaxToken[] tokens, int index)
    {
        for (int current = index - 1; current >= 0; current--)
        {
            if (tokens[current].IsKind(SyntaxKind.ColonToken))
            {
                return true;
            }

            if (tokens[current].IsKind(SyntaxKind.OpenBraceToken)
                || tokens[current].IsKind(SyntaxKind.CloseBraceToken)
                || tokens[current].IsKind(SyntaxKind.SemicolonToken))
            {
                return false;
            }
        }

        return false;
    }

    private static SourceUsage CreateTokenUsage(
        string relativePath,
        SyntaxToken[] tokens,
        int index,
        string kind)
    {
        int end = index;
        while (end + 1 < tokens.Length
            && end - index < 8
            && !IsUsageBoundary(tokens[end + 1]))
        {
            end++;
        }

        return CreateUsage(kind, relativePath, tokens, index, end);
    }

    private static SourceUsage CreateUsage(
        string kind,
        string relativePath,
        SyntaxToken[] tokens,
        int start,
        int end)
    {
        return new SourceUsage(
            kind,
            relativePath,
            NormalizeTokens(tokens, start, end),
            tokens[start].SpanStart);
    }

    private static bool TryGetStaticInstanceType(
        SyntaxToken[] tokens,
        int index,
        IReadOnlyDictionary<string, string> aliases,
        out string staticType)
    {
        staticType = null;
        if (!tokens[index].IsKind(SyntaxKind.IdentifierToken)
            || index + 2 >= tokens.Length
            || !tokens[index + 1].IsKind(SyntaxKind.DotToken)
            || !IsIdentifier(tokens[index + 2], "Instance"))
        {
            return false;
        }

        string candidate = tokens[index].ValueText;
        if (aliases.TryGetValue(candidate, out string aliasedType))
        {
            candidate = aliasedType;
        }

        if (!string.Equals(candidate, "SystemEnvironment", StringComparison.Ordinal)
            && !string.Equals(candidate, "ScriptSettingsManager", StringComparison.Ordinal))
        {
            return false;
        }

        staticType = candidate;
        return true;
    }

    private static bool TryGetInvocationName(
        SyntaxToken[] tokens,
        int dotIndex,
        out string invocationName,
        out int invocationEnd)
    {
        invocationName = null;
        invocationEnd = dotIndex;

        if (dotIndex + 2 >= tokens.Length
            || !tokens[dotIndex + 1].IsKind(SyntaxKind.IdentifierToken)
            || !tokens[dotIndex + 2].IsKind(SyntaxKind.OpenParenToken))
        {
            return false;
        }

        invocationName = tokens[dotIndex + 1].ValueText;
        invocationEnd = FindMatchingToken(
            tokens,
            dotIndex + 2,
            SyntaxKind.OpenParenToken,
            SyntaxKind.CloseParenToken);
        return invocationEnd >= dotIndex + 2;
    }

    private static bool IsPredicateUsage(SyntaxToken[] tokens, int index)
    {
        if (TokenIsDeclarationName(tokens[index]))
        {
            return false;
        }

        return (index > 0 && tokens[index - 1].IsKind(SyntaxKind.DotToken))
            || (index + 1 < tokens.Length && tokens[index + 1].IsKind(SyntaxKind.OpenParenToken));

        static bool TokenIsDeclarationName(SyntaxToken token)
        {
            return (token.Parent is MethodDeclarationSyntax method && method.Identifier.Equals(token))
                || (token.Parent is PropertyDeclarationSyntax property && property.Identifier.Equals(token));
        }
    }

    private static int FindMemberChainStart(SyntaxToken[] tokens, int dotIndex)
    {
        int start = Math.Max(0, dotIndex - 1);
        while (start >= 2
            && tokens[start - 1].IsKind(SyntaxKind.DotToken)
            && tokens[start - 2].IsKind(SyntaxKind.IdentifierToken))
        {
            start -= 2;
        }

        return start;
    }

    private static int FindExpressionEnd(SyntaxToken[] tokens, int start)
    {
        int parentheses = 0;
        int brackets = 0;

        for (int index = start; index < tokens.Length; index++)
        {
            SyntaxToken token = tokens[index];
            if (token.IsKind(SyntaxKind.OpenParenToken))
            {
                parentheses++;
            }
            else if (token.IsKind(SyntaxKind.CloseParenToken))
            {
                if (parentheses == 0)
                {
                    return Math.Max(start, index - 1);
                }

                parentheses--;
                continue;
            }
            else if (token.IsKind(SyntaxKind.OpenBracketToken))
            {
                brackets++;
            }
            else if (token.IsKind(SyntaxKind.CloseBracketToken))
            {
                if (brackets == 0)
                {
                    return Math.Max(start, index - 1);
                }

                brackets--;
                continue;
            }

            if (index > start
                && parentheses == 0
                && brackets == 0
                && IsExpressionBoundary(token))
            {
                return index - 1;
            }
        }

        return tokens.Length - 1;
    }

    private static int FindMatchingToken(
        SyntaxToken[] tokens,
        int start,
        SyntaxKind openKind,
        SyntaxKind closeKind)
    {
        int depth = 0;
        for (int index = start; index < tokens.Length; index++)
        {
            if (tokens[index].IsKind(openKind))
            {
                depth++;
            }
            else if (tokens[index].IsKind(closeKind))
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static bool IsUsageBoundary(SyntaxToken token)
    {
        return token.IsKind(SyntaxKind.CommaToken)
            || token.IsKind(SyntaxKind.CloseParenToken)
            || token.IsKind(SyntaxKind.SemicolonToken)
            || token.IsKind(SyntaxKind.OpenBraceToken)
            || token.IsKind(SyntaxKind.CloseBraceToken);
    }

    private static bool IsExpressionBoundary(SyntaxToken token)
    {
        return IsUsageBoundary(token)
            || token.IsKind(SyntaxKind.AmpersandAmpersandToken)
            || token.IsKind(SyntaxKind.BarBarToken)
            || token.IsKind(SyntaxKind.EqualsEqualsToken)
            || token.IsKind(SyntaxKind.ExclamationEqualsToken)
            || token.IsKind(SyntaxKind.QuestionToken)
            || token.IsKind(SyntaxKind.ColonToken);
    }

    private static string NormalizeTokens(SyntaxToken[] tokens, int start, int end)
    {
        return string.Join(" ", tokens
            .Skip(start)
            .Take(end - start + 1)
            .Select(token => token.Text));
    }

    private static bool IsIdentifier(SyntaxToken token, string value)
    {
        return token.IsKind(SyntaxKind.IdentifierToken)
            && string.Equals(token.ValueText, value, StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> GetTypeAliases(SyntaxToken[] tokens)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int index = 0; index + 3 < tokens.Length; index++)
        {
            if (!tokens[index].IsKind(SyntaxKind.UsingKeyword)
                || !tokens[index + 1].IsKind(SyntaxKind.IdentifierToken)
                || !tokens[index + 2].IsKind(SyntaxKind.EqualsToken))
            {
                continue;
            }

            int semicolon = Array.FindIndex(
                tokens,
                index + 3,
                token => token.IsKind(SyntaxKind.SemicolonToken));
            if (semicolon < 0)
            {
                break;
            }

            string target = tokens
                .Skip(index + 3)
                .Take(semicolon - index - 3)
                .LastOrDefault(token => token.IsKind(SyntaxKind.IdentifierToken))
                .ValueText;

            if (string.Equals(target, "SystemEnvironment", StringComparison.Ordinal)
                || string.Equals(target, "ScriptSettingsManager", StringComparison.Ordinal))
            {
                aliases[tokens[index + 1].ValueText] = target;
            }

            index = semicolon;
        }

        return aliases;
    }

    private static void ScanUsingStaticDirectives(
        string relativePath,
        SyntaxToken[] tokens,
        EnvironmentMigrationSnapshot snapshot)
    {
        for (int index = 0; index + 2 < tokens.Length; index++)
        {
            if (!tokens[index].IsKind(SyntaxKind.UsingKeyword)
                || !tokens[index + 1].IsKind(SyntaxKind.StaticKeyword))
            {
                continue;
            }

            int semicolon = Array.FindIndex(
                tokens,
                index + 2,
                token => token.IsKind(SyntaxKind.SemicolonToken));
            if (semicolon < 0)
            {
                break;
            }

            string target = tokens
                .Skip(index + 2)
                .Take(semicolon - index - 2)
                .LastOrDefault(token => token.IsKind(SyntaxKind.IdentifierToken))
                .ValueText;
            SourceUsage usage = CreateUsage(
                target + ".Instance",
                relativePath,
                tokens,
                index,
                semicolon);

            if (string.Equals(target, "SystemEnvironment", StringComparison.Ordinal))
            {
                snapshot.SystemEnvironmentInstanceUsages.Add(usage);
            }
            else if (string.Equals(target, "ScriptSettingsManager", StringComparison.Ordinal))
            {
                snapshot.ScriptSettingsManagerInstanceUsages.Add(usage);
            }

            index = semicolon;
        }
    }

    private static IEnumerable<SyntaxNode> ParseAllConditionalBranches(EnvironmentMigrationSourceFile sourceFile)
    {
        string source = RewriteRawStringLiterals(sourceFile.Content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n'));
        CSharpSyntaxTree defaultTree = (CSharpSyntaxTree)CSharpSyntaxTree.ParseText(
            source,
            path: sourceFile.RelativePath);
        SyntaxNode defaultRoot = defaultTree.GetRoot();
        string[] symbols = GetConditionalSymbols(defaultRoot);

        yield return defaultRoot;

        foreach (string[] symbolSet in GetConditionalSymbolSets(symbols).Skip(1))
        {
            yield return CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithPreprocessorSymbols(symbolSet),
                sourceFile.RelativePath)
                .GetRoot();
        }
    }

    private static string RewriteRawStringLiterals(string source)
    {
        var rewritten = new StringBuilder(source.Length);
        int copyStart = 0;
        int searchStart = 0;

        while (TryFindRawString(source, searchStart, out RawStringLiteral literal))
        {
            rewritten.Append(source, copyStart, literal.Start - copyStart);
            IReadOnlyList<string> expressions = literal.DollarCount == 0
                ? Array.Empty<string>()
                : GetRawInterpolationExpressions(source, literal);

            if (expressions.Count == 0)
            {
                rewritten.Append("\"\"");
            }
            else
            {
                rewritten.Append("(\"\"");
                foreach (string expression in expressions)
                {
                    rewritten.Append(" + (").Append(expression).Append(')');
                }

                rewritten.Append(')');
            }

            copyStart = literal.End;
            searchStart = literal.End;
        }

        rewritten.Append(source, copyStart, source.Length - copyStart);
        return rewritten.ToString();
    }

    private static bool TryFindRawString(string source, int searchStart, out RawStringLiteral literal)
    {
        for (int index = searchStart; index < source.Length; index++)
        {
            if (source[index] == '/' && index + 1 < source.Length)
            {
                if (source[index + 1] == '/')
                {
                    index = SkipLineComment(source, index + 2, source.Length);
                    continue;
                }

                if (source[index + 1] == '*')
                {
                    index = SkipBlockComment(source, index + 2, source.Length);
                    continue;
                }
            }

            if (source[index] == '\'')
            {
                index = SkipQuotedText(source, index, source.Length);
                continue;
            }

            if (source[index] == '@'
                && ((index + 1 < source.Length && source[index + 1] == '"')
                    || (index + 2 < source.Length && source[index + 1] == '$' && source[index + 2] == '"')))
            {
                int quoteStart = source[index + 1] == '"' ? index + 1 : index + 2;
                index = SkipQuotedText(source, quoteStart, source.Length);
                continue;
            }

            if (source[index] != '"')
            {
                continue;
            }

            int quoteCount = CountRun(source, index, '"');
            if (quoteCount < 3)
            {
                index = SkipQuotedText(source, index, source.Length);
                continue;
            }

            int dollarStart = index;
            while (dollarStart > 0 && source[dollarStart - 1] == '$')
            {
                dollarStart--;
            }

            int closingDelimiter = FindClosingDelimiter(source, index + quoteCount, quoteCount);
            if (closingDelimiter < 0)
            {
                break;
            }

            literal = new RawStringLiteral(
                dollarStart,
                closingDelimiter + quoteCount,
                index + quoteCount,
                closingDelimiter,
                index - dollarStart);
            return true;
        }

        literal = default;
        return false;
    }

    private static int FindClosingDelimiter(string source, int searchStart, int quoteCount)
    {
        for (int index = searchStart; index <= source.Length - quoteCount; index++)
        {
            if (source[index] == '"' && CountRun(source, index, '"') >= quoteCount)
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> GetRawInterpolationExpressions(string source, RawStringLiteral literal)
    {
        var expressions = new List<string>();
        int index = literal.ContentStart;

        while (index < literal.ContentEnd)
        {
            if (source[index] != '{')
            {
                index++;
                continue;
            }

            int openingBraceCount = CountRun(source, index, '{');
            if (openingBraceCount < literal.DollarCount)
            {
                index += openingBraceCount;
                continue;
            }

            int expressionStart = index + openingBraceCount;
            int expressionEnd = FindInterpolationEnd(
                source,
                expressionStart,
                literal.ContentEnd,
                literal.DollarCount);
            if (expressionEnd < 0)
            {
                break;
            }

            expressions.Add(source.Substring(expressionStart, expressionEnd - expressionStart));
            index = expressionEnd + literal.DollarCount;
        }

        return expressions;
    }

    private static int FindInterpolationEnd(string source, int start, int end, int delimiterBraceCount)
    {
        int nestedBraceDepth = 0;

        for (int index = start; index < end; index++)
        {
            if (source[index] == '"' || source[index] == '\'')
            {
                index = SkipQuotedText(source, index, end);
                continue;
            }

            if (source[index] == '/' && index + 1 < end)
            {
                if (source[index + 1] == '/')
                {
                    index = SkipLineComment(source, index + 2, end);
                    continue;
                }

                if (source[index + 1] == '*')
                {
                    index = SkipBlockComment(source, index + 2, end);
                    continue;
                }
            }

            if (source[index] == '{')
            {
                nestedBraceDepth++;
                continue;
            }

            if (source[index] != '}')
            {
                continue;
            }

            int closingBraceCount = CountRun(source, index, '}');
            if (nestedBraceDepth == 0 && closingBraceCount >= delimiterBraceCount)
            {
                return index;
            }

            int bracesToConsume = Math.Min(nestedBraceDepth, closingBraceCount);
            nestedBraceDepth -= bracesToConsume;
            index += closingBraceCount - 1;
        }

        return -1;
    }

    private static int SkipQuotedText(string source, int start, int end)
    {
        char delimiter = source[start];
        bool isVerbatim = delimiter == '"'
            && ((start > 0 && source[start - 1] == '@')
                || (start > 1 && source[start - 2] == '@' && source[start - 1] == '$'));

        for (int index = start + 1; index < end; index++)
        {
            if (!isVerbatim && source[index] == '\\')
            {
                index++;
                continue;
            }

            if (source[index] != delimiter)
            {
                continue;
            }

            if (isVerbatim && index + 1 < end && source[index + 1] == delimiter)
            {
                index++;
                continue;
            }

            return index;
        }

        return end - 1;
    }

    private static int SkipLineComment(string source, int start, int end)
    {
        int index = start;
        while (index < end && source[index] != '\r' && source[index] != '\n')
        {
            index++;
        }

        return index;
    }

    private static int SkipBlockComment(string source, int start, int end)
    {
        for (int index = start; index + 1 < end; index++)
        {
            if (source[index] == '*' && source[index + 1] == '/')
            {
                return index + 1;
            }
        }

        return end - 1;
    }

    private static int CountRun(string source, int start, char value)
    {
        int count = 0;
        while (start + count < source.Length && source[start + count] == value)
        {
            count++;
        }

        return count;
    }

    private static string[] GetConditionalSymbols(SyntaxNode root)
    {
        return root.DescendantTrivia(descendIntoTrivia: true)
            .Select(trivia => trivia.GetStructure())
            .SelectMany(structure => structure switch
            {
                IfDirectiveTriviaSyntax directive => directive.Condition.DescendantTokens(),
                ElifDirectiveTriviaSyntax directive => directive.Condition.DescendantTokens(),
                _ => Enumerable.Empty<SyntaxToken>()
            })
            .Where(token => token.IsKind(SyntaxKind.IdentifierToken)
                && !string.Equals(token.ValueText, "true", StringComparison.Ordinal)
                && !string.Equals(token.ValueText, "false", StringComparison.Ordinal))
            .Select(token => token.ValueText)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string[]> GetConditionalSymbolSets(string[] symbols)
    {
        if (symbols.Length > 8)
        {
            throw new InvalidOperationException(
                $"Source usage scanning supports at most 8 conditional symbols per file, but found {symbols.Length}.");
        }

        int combinations = 1 << symbols.Length;
        for (int mask = 0; mask < combinations; mask++)
        {
            yield return symbols
                .Where((_, index) => (mask & (1 << index)) != 0)
                .ToArray();
        }
    }

    private static void AddCompiledInventories(EnvironmentMigrationSnapshot snapshot)
    {
        Assembly[] productionAssemblies =
        {
            typeof(IEnvironment).Assembly,
            Assembly.Load("Microsoft.Azure.WebJobs.Script.WebHost"),
            Assembly.Load("Microsoft.Azure.WebJobs.Script.Grpc")
        };

        Type environmentExtensions = typeof(IEnvironment).Assembly.GetType(
            "Microsoft.Azure.WebJobs.Script.EnvironmentExtensions",
            throwOnError: true);
        snapshot.EnvironmentExtensionHelpers.AddRange(environmentExtensions
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(FormatMethodSignature));

        var migrationTypes = new HashSet<Type>
        {
            typeof(IEnvironment),
            typeof(SystemEnvironment),
            typeof(ScriptSettingsManager)
        };

        foreach (Type type in productionAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            bool isMigrationType = migrationTypes.Contains(type);
            bool implementsIEnvironment = typeof(IEnvironment).IsAssignableFrom(type);
            if (isMigrationType || implementsIEnvironment)
            {
                snapshot.PublicSignatures.Add(FormatTypeDeclaration(type));
            }

            foreach (MemberInfo member in GetDeclaredPublicMembers(type))
            {
                if (isMigrationType || ReferencesIEnvironment(member))
                {
                    snapshot.PublicSignatures.Add(FormatMemberSignature(member));
                }
            }
        }

        const string testEnvironmentPath = "test/WebJobs.Script.Tests.Shared/TestEnvironment.cs";
        Type testEnvironment = typeof(TestEnvironment);
        snapshot.TestSeams.Add(new SourceUsage(
            "TestEnvironmentSignature",
            testEnvironmentPath,
            FormatTypeDeclaration(testEnvironment),
            position: -1));

        snapshot.TestSeams.AddRange(GetDeclaredPublicMembers(testEnvironment)
            .Select(member => new SourceUsage(
                "TestEnvironmentSignature",
                testEnvironmentPath,
                FormatMemberSignature(member),
                position: -1)));
    }

    private static IEnumerable<MemberInfo> GetDeclaredPublicMembers(Type type)
    {
        return type
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(member => member switch
            {
                MethodInfo method => !method.IsSpecialName,
                ConstructorInfo => true,
                PropertyInfo => true,
                FieldInfo => true,
                EventInfo => true,
                _ => false
            });
    }

    private static bool ReferencesIEnvironment(MemberInfo member)
    {
        return member switch
        {
            ConstructorInfo constructor => constructor.GetParameters().Any(parameter => ReferencesIEnvironment(parameter.ParameterType)),
            MethodInfo method => ReferencesIEnvironment(method.ReturnType)
                || method.GetParameters().Any(parameter => ReferencesIEnvironment(parameter.ParameterType))
                || method.GetGenericArguments().Any(HasIEnvironmentConstraint),
            PropertyInfo property => ReferencesIEnvironment(property.PropertyType)
                || property.GetIndexParameters().Any(parameter => ReferencesIEnvironment(parameter.ParameterType)),
            FieldInfo field => ReferencesIEnvironment(field.FieldType),
            EventInfo @event => ReferencesIEnvironment(@event.EventHandlerType),
            _ => false
        };
    }

    private static bool ReferencesIEnvironment(Type type)
    {
        if (type is null)
        {
            return false;
        }

        if (type == typeof(IEnvironment))
        {
            return true;
        }

        if (type.HasElementType)
        {
            return ReferencesIEnvironment(type.GetElementType());
        }

        return type.IsGenericType
            && type.GetGenericArguments().Any(ReferencesIEnvironment);
    }

    private static bool HasIEnvironmentConstraint(Type genericArgument)
    {
        return genericArgument.GetGenericParameterConstraints().Any(ReferencesIEnvironment);
    }

    private static string FormatMemberSignature(MemberInfo member)
    {
        return member switch
        {
            ConstructorInfo constructor => FormatConstructorSignature(constructor),
            MethodInfo method => FormatMethodSignature(method),
            PropertyInfo property => FormatPropertySignature(property),
            FieldInfo field => $"field {FormatTypeName(field.FieldType)} {FormatTypeName(field.DeclaringType)}.{field.Name}",
            EventInfo @event => $"event {FormatTypeName(@event.EventHandlerType)} {FormatTypeName(@event.DeclaringType)}.{@event.Name}",
            _ => throw new InvalidOperationException($"Unsupported public member kind '{member.MemberType}'.")
        };
    }

    private static string FormatConstructorSignature(ConstructorInfo constructor)
    {
        return $"constructor {FormatTypeName(constructor.DeclaringType)}({FormatParameters(constructor.GetParameters())})";
    }

    private static string FormatMethodSignature(MethodInfo method)
    {
        string genericArguments = method.IsGenericMethodDefinition
            ? $"<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>"
            : string.Empty;
        string constraints = FormatGenericConstraints(method.GetGenericArguments());
        string parameters = FormatParameters(
            method.GetParameters(),
            method.IsDefined(typeof(ExtensionAttribute), inherit: false));

        return $"method {FormatTypeName(method.ReturnType)} {FormatTypeName(method.DeclaringType)}.{method.Name}{genericArguments}({parameters}){constraints}";
    }

    private static string FormatPropertySignature(PropertyInfo property)
    {
        var accessors = new List<string>();
        if (property.GetMethod?.IsPublic == true)
        {
            accessors.Add("get;");
        }

        if (property.SetMethod?.IsPublic == true)
        {
            accessors.Add("set;");
        }

        string indexParameters = property.GetIndexParameters().Length == 0
            ? string.Empty
            : $"[{FormatParameters(property.GetIndexParameters())}]";
        return $"property {FormatTypeName(property.PropertyType)} {FormatTypeName(property.DeclaringType)}.{property.Name}{indexParameters} {{ {string.Join(" ", accessors)} }}";
    }

    private static string FormatTypeDeclaration(Type type)
    {
        string kind = type.IsInterface ? "interface" : type.IsValueType ? "struct" : "class";
        string baseTypes = string.Join(", ", GetDirectBaseTypes(type).Select(FormatTypeName));
        string suffix = string.IsNullOrEmpty(baseTypes) ? string.Empty : $" : {baseTypes}";
        return $"{kind} {FormatTypeName(type)}{suffix}{FormatGenericConstraints(type.GetGenericArguments())}";
    }

    private static IEnumerable<Type> GetDirectBaseTypes(Type type)
    {
        if (type.BaseType is not null && type.BaseType != typeof(object))
        {
            yield return type.BaseType;
        }

        foreach (Type implementedInterface in type.GetInterfaces()
            .Except(type.BaseType?.GetInterfaces() ?? Type.EmptyTypes)
            .OrderBy(interfaceType => interfaceType.FullName, StringComparer.Ordinal))
        {
            yield return implementedInterface;
        }
    }

    private static string FormatParameters(ParameterInfo[] parameters, bool isExtensionMethod = false)
    {
        return string.Join(", ", parameters.Select((parameter, index) =>
        {
            string modifier = parameter.IsOut
                ? "out "
                : parameter.ParameterType.IsByRef
                    ? "ref "
                    : isExtensionMethod && index == 0
                        ? "this "
                        : string.Empty;
            string defaultValue = parameter.HasDefaultValue
                ? $" = {FormatDefaultValue(parameter.DefaultValue)}"
                : string.Empty;
            return $"{modifier}{FormatTypeName(parameter.ParameterType)} {parameter.Name}{defaultValue}";
        }));
    }

    private static string FormatGenericConstraints(Type[] genericArguments)
    {
        return string.Concat(genericArguments
            .Where(argument => argument.IsGenericParameter)
            .Select(argument =>
            {
                var constraints = new List<string>();
                GenericParameterAttributes attributes = argument.GenericParameterAttributes;
                if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                {
                    constraints.Add("class");
                }

                if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                {
                    constraints.Add("struct");
                }

                constraints.AddRange(argument.GetGenericParameterConstraints().Select(FormatTypeName));
                if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0
                    && (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
                {
                    constraints.Add("new()");
                }

                return constraints.Count == 0
                    ? string.Empty
                    : $" where {argument.Name} : {string.Join(", ", constraints)}";
            }));
    }

    private static string FormatDefaultValue(object value)
    {
        return value switch
        {
            null => "null",
            string text => $"\"{text}\"",
            char character => $"'{character}'",
            bool boolean => boolean ? "true" : "false",
            Missing => "missing",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private static string FormatTypeName(Type type)
    {
        if (type is null)
        {
            return "null";
        }

        if (type.IsByRef)
        {
            return FormatTypeName(type.GetElementType());
        }

        if (type.IsArray)
        {
            return $"{FormatTypeName(type.GetElementType())}[{new string(',', type.GetArrayRank() - 1)}]";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (!type.IsGenericType)
        {
            return (type.FullName ?? type.Name).Replace('+', '.');
        }

        string genericName = type.GetGenericTypeDefinition().FullName;
        genericName = genericName.Substring(0, genericName.IndexOf('`')).Replace('+', '.');
        return $"{genericName}<{string.Join(", ", type.GetGenericArguments().Select(FormatTypeName))}>";
    }

    private static bool IsGeneratedPath(string relativePath)
    {
        return NormalizePath(relativePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsEnvironmentPredicateName(string name)
    {
        if (EnvironmentPredicateNames.Contains(name))
        {
            return true;
        }

        bool hasPredicatePrefix = name.StartsWith("Has", StringComparison.Ordinal)
            || name.StartsWith("Is", StringComparison.Ordinal)
            || name.StartsWith("Should", StringComparison.Ordinal)
            || name.StartsWith("Use", StringComparison.Ordinal);

        return hasPredicatePrefix
            && EnvironmentPredicateMarkers.Any(marker => name.Contains(marker, StringComparison.Ordinal));
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private readonly struct RawStringLiteral
    {
        public RawStringLiteral(
            int start,
            int end,
            int contentStart,
            int contentEnd,
            int dollarCount)
        {
            Start = start;
            End = end;
            ContentStart = contentStart;
            ContentEnd = contentEnd;
            DollarCount = dollarCount;
        }

        public int Start { get; }

        public int End { get; }

        public int ContentStart { get; }

        public int ContentEnd { get; }

        public int DollarCount { get; }
    }
}

internal sealed class EnvironmentMigrationSourceFile
{
    public EnvironmentMigrationSourceFile(string relativePath, string content)
    {
        RelativePath = relativePath;
        Content = content;
    }

    public string RelativePath { get; }

    public string Content { get; }
}

internal sealed class EnvironmentMigrationSnapshot
{
    public List<string> EnvironmentExtensionHelpers { get; } = new();

    public List<SourceUsage> DirectEnvironmentReads { get; } = new();

    public List<SourceUsage> DirectEnvironmentWrites { get; } = new();

    public List<SourceUsage> IEnvironmentUsages { get; } = new();

    public List<SourceUsage> SystemEnvironmentInstanceUsages { get; } = new();

    public List<SourceUsage> ScriptSettingsManagerInstanceUsages { get; } = new();

    public List<SourceUsage> EnvironmentPredicateUsages { get; } = new();

    public List<string> PublicSignatures { get; } = new();

    public List<SourceUsage> TestSeams { get; } = new();

    public void MergeDistinct(EnvironmentMigrationSnapshot other)
    {
        EnvironmentExtensionHelpers.AddRange(other.EnvironmentExtensionHelpers
            .Except(EnvironmentExtensionHelpers, StringComparer.Ordinal));
        DirectEnvironmentReads.AddRange(GetDistinctOccurrences(other.DirectEnvironmentReads));
        DirectEnvironmentWrites.AddRange(GetDistinctOccurrences(other.DirectEnvironmentWrites));
        IEnvironmentUsages.AddRange(GetDistinctOccurrences(other.IEnvironmentUsages));
        SystemEnvironmentInstanceUsages.AddRange(GetDistinctOccurrences(other.SystemEnvironmentInstanceUsages));
        ScriptSettingsManagerInstanceUsages.AddRange(GetDistinctOccurrences(other.ScriptSettingsManagerInstanceUsages));
        EnvironmentPredicateUsages.AddRange(GetDistinctOccurrences(other.EnvironmentPredicateUsages));
        PublicSignatures.AddRange(other.PublicSignatures
            .Except(PublicSignatures, StringComparer.Ordinal));
        TestSeams.AddRange(GetDistinctOccurrences(other.TestSeams));
    }

    private static IEnumerable<SourceUsage> GetDistinctOccurrences(IEnumerable<SourceUsage> usages)
    {
        return usages
            .GroupBy(
                usage => $"{usage.Kind}|{usage.RelativePath}|{usage.Position}",
                StringComparer.Ordinal)
            .Select(group => group.First());
    }
}

internal sealed class SourceUsage
{
    public SourceUsage(string kind, string relativePath, string syntax, int position)
    {
        Kind = kind;
        RelativePath = relativePath;
        Syntax = syntax;
        Position = position;
    }

    public string Kind { get; }

    public string RelativePath { get; }

    public string Syntax { get; }

    public int Position { get; }
}
