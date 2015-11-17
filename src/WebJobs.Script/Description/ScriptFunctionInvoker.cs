// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    // TODO: make this internal
    public class ScriptFunctionInvoker : IFunctionInvoker
    {
        private const string BashPathEnvironmentKey = "AzureWebJobs_BashPath";
        private const string ProgramFiles64bitKey = "ProgramW6432";
        private static string[] _supportedScriptTypes = new string[] { "ps1", "cmd", "bat", "py", "php", "sh", "fsx" };
        private readonly string _scriptFilePath;
        private readonly string _scriptType;

        public ScriptFunctionInvoker(string scriptFilePath)
        {
            _scriptFilePath = scriptFilePath;
            _scriptType = Path.GetExtension(_scriptFilePath).ToLower().TrimStart('.');
        }

        public static bool IsSupportedScriptType(string extension)
        {
            string scriptType = extension.ToLower().TrimStart('.');
            return _supportedScriptTypes.Contains(scriptType);
        }

        public async Task Invoke(object[] parameters)
        {
            string input = parameters[0].ToString();
            TextWriter textWriter = (TextWriter)parameters[1];

            switch (_scriptType)
            {
                case "ps1":
                    await InvokePowerShellScript(input, textWriter);
                    break;
                case "cmd":
                case "bat":
                    await InvokeWindowsBatchScript(input, textWriter);
                    break;
                case "py":
                    await InvokePythonScript(input, textWriter);
                    break;
                case "php":
                    await InvokePhpScript(input, textWriter);
                    break;
                case "sh":
                    await InvokeBashScript(input, textWriter);
                    break;
                case "fsx":
                    await InvokeFSharpScript(input, textWriter);
                    break;
            }
        }

        internal Task InvokeFSharpScript(string input, TextWriter textWriter)
        {
            string scriptHostArguments = string.Format("/c fsi.exe {0}", _scriptFilePath);

            return InvokeScriptHostCore("cmd", scriptHostArguments, textWriter, input);
        }

        internal Task InvokeBashScript(string input, TextWriter textWriter)
        {
            string scriptHostArguments = string.Format("{0}", _scriptFilePath);
            string bashPath = ResolveBashPath();

            return InvokeScriptHostCore(bashPath, scriptHostArguments, textWriter, input);
        }

        internal Task InvokePhpScript(string input, TextWriter textWriter)
        {
            string scriptHostArguments = string.Format("{0}", _scriptFilePath);

            return InvokeScriptHostCore("php.exe", scriptHostArguments, textWriter, input);
        }

        internal Task InvokePythonScript(string input, TextWriter textWriter)
        {
            string scriptHostArguments = string.Format("{0}", _scriptFilePath);

            return InvokeScriptHostCore("python.exe", scriptHostArguments, textWriter, input);
        }

        internal Task InvokePowerShellScript(string input, TextWriter textWriter)
        {
            string scriptHostArguments = string.Format("-ExecutionPolicy RemoteSigned -File {0}", _scriptFilePath);

            return InvokeScriptHostCore("PowerShell.exe", scriptHostArguments, textWriter, input);
        }

        internal Task InvokeWindowsBatchScript(string input, TextWriter textWriter)
        {
            string scriptHostArguments = string.Format("/c {0}", _scriptFilePath);

            return InvokeScriptHostCore("cmd", scriptHostArguments, textWriter, input);
        }

        internal Task InvokeScriptHostCore(string path, string arguments, TextWriter textWriter, string stdin = null)
        {
            string workingDirectory = Path.GetDirectoryName(_scriptFilePath);

            // TODO
            // - put a timeout on how long we wait?
            // - need to periodically flush the standard out to the TextWriter
            // - need to handle stderr as well
            Process process = CreateProcess(path, workingDirectory, arguments);
            process.Start();
            if (stdin != null)
            {
                process.StandardInput.WriteLine(stdin);
                process.StandardInput.Flush();
            }
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();
                throw new ApplicationException(error);
            }

            // write the results to the Dashboard
            string output = process.StandardOutput.ReadToEnd();
            textWriter.Write(output);

            return Task.FromResult(0);
        }

        internal Process CreateProcess(string path, string workingDirectory, string arguments, IDictionary<string, string> environmentVariables = null)
        {
            // TODO: need to set encoding on stdout/stderr?
            var psi = new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                ErrorDialog = false,
                Arguments = arguments
            };

            if (environmentVariables != null)
            {
                foreach (var pair in environmentVariables)
                {
                    psi.EnvironmentVariables[pair.Key] = pair.Value;
                }
            }

            return new Process()
            {
                StartInfo = psi
            };
        }

        internal static string ResolveBashPath()
        {
            // first see if the path is specified as an evironment variable
            // (useful for running locally outside of Azure)
            string path = Environment.GetEnvironmentVariable(BashPathEnvironmentKey);
            if (!string.IsNullOrEmpty(path))
            {
                path = Path.Combine(path, "bash.exe");
                if (File.Exists(path))
                {
                    return path;
                }
            }

            // attempt to resolve the path relative to ProgramFiles based on
            // standard Azure WebApp images
            string relativePath = Path.Combine("Git", "bin", "bash.exe");
            return ResolveRelativePathToProgramFiles(relativePath, relativePath, "bash.exe");
        }

        private static string ResolveRelativePathToProgramFiles(string relativeX86Path, string relativeX64Path, string target)
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string path = Path.Combine(programFiles, relativeX86Path);
            if (!File.Exists(path))
            {
                programFiles = Environment.GetEnvironmentVariable(ProgramFiles64bitKey) ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                path = Path.Combine(programFiles, relativeX64Path);
            }

            if (!File.Exists(path))
            {
                throw new InvalidOperationException(string.Format("Unable to locate '{0}'. Make sure it is installed.", target));
            }

            return path;
        }
    }
}
