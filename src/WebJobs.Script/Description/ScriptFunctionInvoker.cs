// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;

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
        private readonly Collection<OutputBinding> _outputBindings;

        public ScriptFunctionInvoker(string scriptFilePath, JObject functionConfiguration)
        {
            _scriptFilePath = scriptFilePath;
            _scriptType = Path.GetExtension(_scriptFilePath).ToLower().TrimStart('.');

            // parse the output bindings
            JArray outputs = (JArray)functionConfiguration["outputs"];
            _outputBindings = OutputBinding.GetOutputBindings(outputs);
        }

        public static bool IsSupportedScriptType(string extension)
        {
            string scriptType = extension.ToLower().TrimStart('.');
            return _supportedScriptTypes.Contains(scriptType);
        }

        public async Task Invoke(object[] parameters)
        {
            string input = parameters[0].ToString();
            TraceWriter traceWriter = (TraceWriter)parameters[1];
            IBinder binder = (IBinder)parameters[2];

            string scriptHostArguments;
            switch (_scriptType)
            {
                case "ps1":
                    scriptHostArguments = string.Format("-ExecutionPolicy RemoteSigned -File {0}", _scriptFilePath);
                    await ExecuteScriptAsync("PowerShell.exe", scriptHostArguments, traceWriter, binder, input);
                    break;
                case "cmd":
                case "bat":
                    scriptHostArguments = string.Format("/c {0}", _scriptFilePath);
                    await ExecuteScriptAsync("cmd", scriptHostArguments, traceWriter, binder, input);
                    break;
                case "py":
                    scriptHostArguments = string.Format("{0}", _scriptFilePath);
                    await ExecuteScriptAsync("python.exe", scriptHostArguments, traceWriter, binder, input);
                    break;
                case "php":
                    scriptHostArguments = string.Format("{0}", _scriptFilePath);
                    await ExecuteScriptAsync("php.exe", scriptHostArguments, traceWriter, binder, input);
                    break;
                case "sh":
                    scriptHostArguments = string.Format("{0}", _scriptFilePath);
                    string bashPath = ResolveBashPath();
                    await ExecuteScriptAsync(bashPath, scriptHostArguments, traceWriter, binder, input);
                    break;
                case "fsx":
                    scriptHostArguments = string.Format("/c fsi.exe {0}", _scriptFilePath);
                    await ExecuteScriptAsync("cmd", scriptHostArguments, traceWriter, binder, input);
                    break;
            }
        }

        internal async Task ExecuteScriptAsync(string path, string arguments, TraceWriter traceWriter, IBinder binder, string stdin = null)
        {
            string instanceId = Guid.NewGuid().ToString();
            string workingDirectory = Path.GetDirectoryName(_scriptFilePath);

            // if there are any binding parameters in the output bindings,
            // parse the input as json to get the binding data
            Dictionary<string, string> bindingData = new Dictionary<string, string>();
            bindingData["InstanceId"] = instanceId;
            if (_outputBindings.Any(p => p.HasBindingParameters))
            {
                try
                {
                    JObject parsed = JObject.Parse(stdin);
                    bindingData = parsed.ToObject<Dictionary<string, string>>();
                }
                catch
                {
                    // it's not an error if the incoming message isn't JSON
                    // there are cases where there will be output binding parameters
                    // that don't bind to JSON properties
                }
            }

            // if there are any output bindings declared, set up the temporary
            // output directory
            string rootOutputPath = Path.Combine(Path.GetTempPath(), "webjobs", "output");
            string functionInstanceOutputPath = Path.Combine(rootOutputPath, instanceId);
            if (_outputBindings.Count > 0)
            {
                Directory.CreateDirectory(functionInstanceOutputPath);
            }

            // setup the script execution environment
            Dictionary<string, string> environmentVariables = new Dictionary<string, string>();
            environmentVariables["InstanceId"] = instanceId;
            environmentVariables["OutputPath"] = functionInstanceOutputPath;
            foreach (var outputBinding in _outputBindings)
            {
                environmentVariables[outputBinding.Name] = Path.Combine(functionInstanceOutputPath, outputBinding.Name);
            }

            // TODO
            // - put a timeout on how long we wait?
            // - need to periodically flush the standard out to the TraceWriter
            // - need to handle stderr as well
            Process process = CreateProcess(path, workingDirectory, arguments, environmentVariables);
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
            traceWriter.Verbose(output);

            // process output bindings
            foreach (var outputBinding in _outputBindings)
            {
                string outFilePath = System.IO.Path.Combine(functionInstanceOutputPath, outputBinding.Name);
                using (FileStream stream = File.OpenRead(outFilePath))
                {
                    await outputBinding.BindAsync(binder, stream, bindingData);
                }
            }

            // clean up the output directory
            if (_outputBindings.Any() && Directory.Exists(functionInstanceOutputPath))
            {
                Directory.Delete(functionInstanceOutputPath, recursive: true);
            }
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
