using System;
using System.Diagnostics;
using System.IO;
using FunctionsColdStartProfileAnalyzer.Analyzer;

namespace FunctionsColdStartProfileAnalyzer.JitTrace
{
    internal static class JitTraceGenerator
    {
        // The functions perf lab agent machine has pgo tool present at these locations.
        private const string WindowsPgoToolPath = @"C:\azure_functions_temp\artifacts\bin\coreclr\windows.x64.Debug\dotnet-pgo\dotnet-pgo.exe";
        private const string LinuxPgoToolPath = @"/var/tmp/azure_functions_temp/artifacts/bin/coreclr/linux.x64.Debug/dotnet-pgo/dotnet-pgo";

        internal static void CreateJitTrace(Summary summary, RunOptions runOptions)
        {
            ArgumentNullException.ThrowIfNull(summary);

            try
            {
                var dotnetPgoExecutablePath = Environment.OSVersion.Platform == PlatformID.Win32NT ? WindowsPgoToolPath : LinuxPgoToolPath;

                if (!File.Exists(dotnetPgoExecutablePath))
                {
                    Console.WriteLine($"File {dotnetPgoExecutablePath} does not exist.");
                    return;
                }

                RunProcess(dotnetPgoExecutablePath, BuildCommandLineArguments(summary, runOptions));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred while creating JIT trace: {ex.Message}");
            }
        }

        private static string BuildCommandLineArguments(Summary summary, RunOptions runOptions)
        {
            var outputJitTraceFilePath = Path.ChangeExtension(summary.TraceFilePath, ".jittrace");

            var excludeAfter = GetBufferedExcludeAfter(summary.ExcludeAfter, summary.SessionEndTimeRelativeMSec, runOptions.JitTraceExcludeAfterBufferInMilliSeconds);
            var excludeAfterArg = $"--exclude-events-after {excludeAfter} ";

            var excludeBeforeTime = GetBufferedExcludeBefore(summary.ExcludeBefore, runOptions.JitTraceExcludeBeforeBufferInMilliSeconds);
            string excludeBeforeArg = $"--exclude-events-before {excludeBeforeTime} ";
            return $"create-jittrace --includeReadyToRun --sorted -t {summary.TraceFilePath} -o {outputJitTraceFilePath} " +
                excludeBeforeArg +
                excludeAfterArg +
                $"--verbose diagnostic";
        }

        public static double GetBufferedExcludeBefore(double value, int bufferInMilliSeconds)
        {
            return Math.Max(0, value - bufferInMilliSeconds);
        }

        public static double GetBufferedExcludeAfter(double endTime, double sessionEndRelativeTs, int bufferInMilliSeconds)
        {
            return endTime + bufferInMilliSeconds;
        }

        private static void RunProcess(string executablePath, string arguments)
        {
            Console.WriteLine($"Running process: {executablePath} {arguments}");

            var processStartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.OutputDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit(TimeSpan.FromMinutes(10));

            if (process.ExitCode != 0)
            {
                Console.WriteLine($"dotnet-pgo.exe exited with code {process.ExitCode}. executablePath:{executablePath}, arguments:{arguments}");
            }
        }
    }
}
