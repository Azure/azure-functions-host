// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script.PublicApi.Tests;

/// <summary>
/// Reads the explicit project list of the official Windows pack job without adding a YAML dependency.
/// </summary>
internal static class PackJobReader
{
    /// <summary>
    /// Reads every project entry packed by the supplied pipeline template.
    /// </summary>
    /// <param name="templateRelativePath">The repository-relative template path.</param>
    /// <returns>The packed project globs, in file order.</returns>
    public static IReadOnlyList<string> ReadPackedProjects(string templateRelativePath)
    {
        string path = RepositoryPaths.Combine(templateRelativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The manifest references pack job template '{templateRelativePath}', which does not exist.", path);
        }

        return ReadPackedProjects(File.ReadAllLines(path));
    }

    /// <summary>
    /// Reads every project entry packed by the supplied pipeline template content.
    /// </summary>
    /// <param name="lines">The template lines.</param>
    /// <returns>The packed project globs, in file order.</returns>
    public static IReadOnlyList<string> ReadPackedProjects(IReadOnlyList<string> lines)
    {
        var projects = new List<string>();

        foreach ((int start, int end) in GetStepRanges(lines))
        {
            if (!IsPackStep(lines, start, end))
            {
                continue;
            }

            projects.AddRange(ReadProjectsBlock(lines, start, end));
        }

        return projects;
    }

    private static IEnumerable<(int Start, int End)> GetStepRanges(IReadOnlyList<string> lines)
    {
        int stepStart = -1;
        int stepIndent = -1;

        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index];
            if (IsBlankOrComment(line))
            {
                continue;
            }

            int indent = GetIndent(line);
            bool isListItem = line.TrimStart().StartsWith("- ", StringComparison.Ordinal);

            if (stepStart >= 0 && (indent < stepIndent || (indent == stepIndent && isListItem)))
            {
                yield return (stepStart, index);
                stepStart = -1;
                stepIndent = -1;
            }

            if (isListItem && line.TrimStart().StartsWith("- task:", StringComparison.Ordinal))
            {
                stepStart = index;
                stepIndent = indent;
            }
        }

        if (stepStart >= 0)
        {
            yield return (stepStart, lines.Count);
        }
    }

    private static bool IsPackStep(IReadOnlyList<string> lines, int start, int end)
    {
        for (int index = start; index < end; index++)
        {
            if (string.Equals(lines[index].Trim(), "custom: pack", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> ReadProjectsBlock(IReadOnlyList<string> lines, int start, int end)
    {
        for (int index = start; index < end; index++)
        {
            string trimmed = lines[index].Trim();

            if (trimmed.StartsWith("projects:", StringComparison.Ordinal))
            {
                string inline = trimmed["projects:".Length..].Trim();
                if (!string.IsNullOrEmpty(inline) && !string.Equals(inline, "|", StringComparison.Ordinal))
                {
                    yield return inline;
                    continue;
                }

                int blockIndent = GetIndent(lines[index]);
                for (int entry = index + 1; entry < end; entry++)
                {
                    if (IsBlankOrComment(lines[entry]))
                    {
                        continue;
                    }

                    if (GetIndent(lines[entry]) <= blockIndent)
                    {
                        break;
                    }

                    yield return lines[entry].Trim();
                }
            }
        }
    }

    private static bool IsBlankOrComment(string line)
    {
        string trimmed = line.Trim();
        return trimmed.Length == 0 || trimmed.StartsWith('#');
    }

    private static int GetIndent(string line)
    {
        int indent = 0;
        while (indent < line.Length && line[indent] == ' ')
        {
            indent++;
        }

        return indent;
    }
}
