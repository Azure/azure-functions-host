// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration;

internal static class IntegrationTestPrintLogger
{
    private const string Prefix = "[IntegrationTestLifecycle]";
    private static readonly AsyncLocal<string> CurrentTestName = new();

    internal static void TestStart(string testName)
    {
        CurrentTestName.Value = testName;
        Console.WriteLine($"{Prefix} TestStart: {testName}");
    }

    internal static void TestEnd(string testName)
    {
        Console.WriteLine($"{Prefix} TestEnd: {testName}");
        CurrentTestName.Value = null;
    }

    internal static void FixtureSetupStart(string fixtureName)
    {
        Console.WriteLine($"{Prefix} FixtureSetupStart: {fixtureName}");
    }

    internal static void CopiedRootPath(string fixtureName, string path)
    {
        Console.WriteLine($"{Prefix} Fixture: {fixtureName}; CopiedRootPath: {path}");
    }

    internal static void FixtureSetupEnd(string fixtureName)
    {
        Console.WriteLine($"{Prefix} FixtureSetupEnd: {fixtureName}");
    }

    internal static void FixtureDisposeStart(string fixtureName)
    {
        Console.WriteLine($"{Prefix} FixtureDisposeStart: {fixtureName}");
    }

    internal static void FixtureDisposeEnd(string fixtureName)
    {
        Console.WriteLine($"{Prefix} FixtureDisposeEnd: {fixtureName}");
    }

    internal static void DirectoryDelete(string directory, string fixtureName)
    {
        Console.WriteLine($"{Prefix} DirectoryDelete: {directory}, Fixture: {fixtureName}");
    }

    private static string ResolveTestName()
    {
        return string.IsNullOrEmpty(CurrentTestName.Value) ? "UnknownTest" : CurrentTestName.Value;
    }
}
