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
        // TODO: Add support for php, sh
        private static string[] _supportedScriptTypes = new string[] { "ps1", "cmd", "bat", "py" };
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
            }
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
    }
}
