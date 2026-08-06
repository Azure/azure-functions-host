// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Config.Tests;

internal static class EnvironmentContractTestHostRunner
{
    private const string TestHostProjectName = "WebJobs.Script.Tests.EnvironmentVariables.TestHost.csproj";

#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    public static async Task<T> RunScenarioAsync<T>(
        string scenario, string argument = null, [CallerFilePath] string sourceFilePath = "")
    {
        string repositoryRoot = FindRepositoryRoot(sourceFilePath);
        string testHostProject = Path.Combine(
            repositoryRoot,
            "test",
            "WebJobs.Script.Tests.EnvironmentVariables.TestHost",
            TestHostProjectName);
        string dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrEmpty(dotnetHost))
        {
            dotnetHost = "dotnet";
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = dotnetHost,
            WorkingDirectory = repositoryRoot,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(BuildConfiguration);
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(testHostProject);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(scenario);
        if (argument is not null)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the environment-variable contract test host.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            string timeoutOutput = await standardOutput;
            string timeoutError = await standardError;
            throw new TimeoutException(
                $"Environment-variable contract scenario '{scenario}' exceeded 3 minutes."
                + $"{Environment.NewLine}{timeoutError}{Environment.NewLine}{timeoutOutput}");
        }

        string output = await standardOutput;
        string error = await standardError;
        Assert.True(
            process.ExitCode == 0,
            $"Environment-variable contract scenario '{scenario}' exited with code {process.ExitCode}.{Environment.NewLine}{error}{Environment.NewLine}{output}");

        string resultLine = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .SingleOrDefault(line => line.StartsWith(
                EnvironmentVariablesConfigurationTestContracts.ResultPrefix,
                StringComparison.Ordinal));
        Assert.False(
            string.IsNullOrEmpty(resultLine),
            $"Environment-variable contract scenario '{scenario}' did not emit a result.{Environment.NewLine}{error}{Environment.NewLine}{output}");

        T result = JsonSerializer.Deserialize<T>(
            resultLine[EnvironmentVariablesConfigurationTestContracts.ResultPrefix.Length..],
            EnvironmentBehaviorParityTestContracts.SerializerOptions);
        return result
            ?? throw new InvalidOperationException($"Unable to deserialize environment-variable contract scenario '{scenario}'.");
    }

    private static string FindRepositoryRoot(string sourceFilePath)
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

        DirectoryInfo directory = new(startPath);
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
}
