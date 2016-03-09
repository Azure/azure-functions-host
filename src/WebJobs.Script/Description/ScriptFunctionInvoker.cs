// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    // TODO: make this internal
    public class ScriptFunctionInvoker : ScriptFunctionInvokerBase
    {
        private const string BashPathEnvironmentKey = "AzureWebJobs_BashPath";
        private const string ProgramFiles64bitKey = "ProgramW6432";
        private static string[] _supportedScriptTypes = new string[] { "ps1", "cmd", "bat", "py", "php", "sh", "fsx" };
        private readonly string _scriptFilePath;
        private readonly string _scriptType;
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;

        internal ScriptFunctionInvoker(string scriptFilePath, ScriptHost host, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
            : base(host, functionMetadata)
        {
            _scriptFilePath = scriptFilePath;
            _scriptType = Path.GetExtension(_scriptFilePath).ToLower(CultureInfo.InvariantCulture).TrimStart('.');
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
        }

        public static bool IsSupportedScriptType(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                throw new ArgumentNullException("extension");
            }

            string scriptType = extension.ToLower(CultureInfo.InvariantCulture).TrimStart('.');
            return _supportedScriptTypes.Contains(scriptType);
        }

        public override async Task Invoke(object[] parameters)
        {
            string scriptHostArguments;
            switch (_scriptType)
            {
                case "ps1":
                    scriptHostArguments = string.Format("-ExecutionPolicy RemoteSigned -File \"{0}\"", _scriptFilePath);
                    await ExecuteScriptAsync("PowerShell.exe", scriptHostArguments, parameters);
                    break;
                case "cmd":
                case "bat":
                    scriptHostArguments = string.Format("/c \"{0}\"", _scriptFilePath);
                    await ExecuteScriptAsync("cmd", scriptHostArguments, parameters);
                    break;
                case "py":
                    scriptHostArguments = string.Format("\"{0}\"", _scriptFilePath);
                    await ExecuteScriptAsync("python.exe", scriptHostArguments, parameters);
                    break;
                case "php":
                    scriptHostArguments = string.Format("\"{0}\"", _scriptFilePath);
                    await ExecuteScriptAsync("php.exe", scriptHostArguments, parameters);
                    break;
                case "sh":
                    scriptHostArguments = string.Format("\"{0}\"", _scriptFilePath);
                    string bashPath = ResolveBashPath();
                    await ExecuteScriptAsync(bashPath, scriptHostArguments, parameters);
                    break;
                case "fsx":
                    scriptHostArguments = string.Format("/c fsi.exe \"{0}\"", _scriptFilePath);
                    await ExecuteScriptAsync("cmd", scriptHostArguments, parameters);
                    break;
            }
        }

        internal async Task ExecuteScriptAsync(string path, string arguments, object[] invocationParameters)
        {
            object input = invocationParameters[0];
            TraceWriter traceWriter = (TraceWriter)invocationParameters[1];
            IBinder binder = (IBinder)invocationParameters[2];
            ExecutionContext functionExecutionContext = (ExecutionContext)invocationParameters[3];

            // perform any required input conversions
            string stdin = null;
            if (input != null)
            {
                HttpRequestMessage request = input as HttpRequestMessage;
                if (request != null)
                {
                    if (request.Content != null && request.Content.Headers.ContentLength > 0)
                    {
                        stdin = ((HttpRequestMessage)input).Content.ReadAsStringAsync().Result;
                    } 
                }
                else
                {
                    stdin = input.ToString();
                }
            }

            TraceWriter.Verbose(string.Format("Function started"));

            string invocationId = functionExecutionContext.InvocationId.ToString();
            string workingDirectory = Path.GetDirectoryName(_scriptFilePath);
            string functionInstanceOutputPath = Path.Combine(Path.GetTempPath(), "Functions", "Binding", invocationId);

            Dictionary<string, string> environmentVariables = new Dictionary<string, string>();
            InitializeEnvironmentVariables(environmentVariables, functionInstanceOutputPath, input, _outputBindings, functionExecutionContext);

            // if there are any parameters in the bindings,
            // parse the input as json to get the binding data
            Dictionary<string, string> bindingData = new Dictionary<string, string>();
            if (_outputBindings.Any(p => p.HasBindingParameters) ||
                _inputBindings.Any(p => p.HasBindingParameters))
            {
                bindingData = GetBindingData(stdin);
            }
            bindingData["InvocationId"] = invocationId;

            await ProcessInputBindingsAsync(functionInstanceOutputPath, binder, bindingData, environmentVariables);

            // TODO
            // - put a timeout on how long we wait?
            // - need to periodically flush the standard out to the TraceWriter
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
                TraceWriter.Error(error);
                TraceWriter.Verbose(string.Format("Function completed (Failure)"));
                throw new ApplicationException(error);
            }

            string output = process.StandardOutput.ReadToEnd();
            TraceWriter.Verbose(output);
            traceWriter.Verbose(output);

            await ProcessOutputBindingsAsync(functionInstanceOutputPath, _outputBindings, input, binder, bindingData);

            TraceWriter.Verbose(string.Format("Function completed (Success)"));
        }

        private void InitializeEnvironmentVariables(Dictionary<string, string> environmentVariables, string functionInstanceOutputPath, object input, Collection<FunctionBinding> outputBindings, ExecutionContext context)
        {
            environmentVariables["InvocationId"] = context.InvocationId.ToString();

            foreach (var outputBinding in _outputBindings)
            {
                environmentVariables[outputBinding.Name] = Path.Combine(functionInstanceOutputPath, outputBinding.Name);
            }

            Type triggerParameterType = input.GetType();
            if (triggerParameterType == typeof(HttpRequestMessage))
            {
                HttpRequestMessage request = (HttpRequestMessage)input;
                Dictionary<string, string> queryParams = request.GetQueryNameValuePairs().ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
                foreach (var queryParam in queryParams)
                {
                    string varName = string.Format(CultureInfo.InvariantCulture, "REQ_QUERY_{0}", queryParam.Key.ToUpperInvariant());
                    environmentVariables[varName] = queryParam.Value;
                }

                foreach (var header in request.Headers)
                {
                    string varName = string.Format(CultureInfo.InvariantCulture, "REQ_HEADERS_{0}", header.Key.ToUpperInvariant());
                    environmentVariables[varName] = header.Value.First();
                }
            }
        }

        private async Task ProcessInputBindingsAsync(string functionInstanceOutputPath, IBinder binder, Dictionary<string, string> bindingData, Dictionary<string, string> environmentVariables)
        {
            // if there are any input or output bindings declared, set up the temporary
            // output directory
            var nonTriggerInputBindings = _inputBindings.Where(p => !p.IsTrigger);
            if (_outputBindings.Count > 0 || nonTriggerInputBindings.Any())
            {
                Directory.CreateDirectory(functionInstanceOutputPath);
            }

            // process input bindings
            foreach (var inputBinding in nonTriggerInputBindings)
            {
                string filePath = System.IO.Path.Combine(functionInstanceOutputPath, inputBinding.Name);
                using (FileStream stream = File.OpenWrite(filePath))
                {
                    BindingContext bindingContext = new BindingContext
                    {
                        Binder = binder,
                        BindingData = bindingData,
                        Value = stream
                    };
                    await inputBinding.BindAsync(bindingContext);
                }

                environmentVariables[inputBinding.Name] = Path.Combine(functionInstanceOutputPath, inputBinding.Name);
            }
        }

        private static async Task ProcessOutputBindingsAsync(string functionInstanceOutputPath, Collection<FunctionBinding> outputBindings,
            object input, IBinder binder, Dictionary<string, string> bindingData)
        {
            if (outputBindings == null)
            {
                return;
            }

            try
            {
                foreach (var outputBinding in outputBindings)
                {
                    string filePath = System.IO.Path.Combine(functionInstanceOutputPath, outputBinding.Name);
                    if (File.Exists(filePath))
                    {
                        using (FileStream stream = File.OpenRead(filePath))
                        {
                            BindingContext bindingContext = new BindingContext
                            {
                                Input = input,
                                Binder = binder,
                                BindingData = bindingData,
                                Value = stream
                            };
                            await outputBinding.BindAsync(bindingContext);
                        }
                    }
                }
            }
            finally
            {
                // clean up the output directory
                if (outputBindings.Any() && Directory.Exists(functionInstanceOutputPath))
                {
                    Directory.Delete(functionInstanceOutputPath, recursive: true);
                }
            }
        }

        internal static Process CreateProcess(string path, string workingDirectory, string arguments, IDictionary<string, string> environmentVariables = null)
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
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Unable to locate '{0}'. Make sure it is installed.", target));
            }

            return path;
        }
    }
}
