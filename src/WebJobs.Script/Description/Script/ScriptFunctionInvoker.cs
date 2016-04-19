﻿// Copyright (c) .NET Foundation. All rights reserved.
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
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    // TODO: make this internal
    public class ScriptFunctionInvoker : FunctionInvokerBase
    {
        private const string FsiPathEnvironmentKey = "AzureWebJobs_FsiPath";
        private const string BashPathEnvironmentKey = "AzureWebJobs_BashPath";
        private const string ProgramFiles64bitKey = "ProgramW6432";
        private static ScriptType[] _supportedScriptTypes = new ScriptType[] { ScriptType.Powershell, ScriptType.WindowsBatch, ScriptType.Python, ScriptType.PHP, ScriptType.Bash, ScriptType.FSharp };
        private readonly string _scriptFilePath;
        private readonly IMetricsLogger _metrics;

        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;

        internal ScriptFunctionInvoker(string scriptFilePath, ScriptHost host, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
            : base(host, functionMetadata)
        {
            _scriptFilePath = scriptFilePath;
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _metrics = host.ScriptConfig.HostConfig.GetService<IMetricsLogger>();
        }

        public static bool IsSupportedScriptType(ScriptType scriptType)
        {
            return _supportedScriptTypes.Contains(scriptType);
        }

        public override async Task Invoke(object[] parameters)
        {
            string scriptHostArguments;
            switch (Metadata.ScriptType)
            {
                case ScriptType.Powershell:
                    scriptHostArguments = string.Format("-ExecutionPolicy RemoteSigned -File \"{0}\"", _scriptFilePath);
                    await ExecuteScriptAsync("PowerShell.exe", scriptHostArguments, parameters);
                    break;
                case ScriptType.WindowsBatch:
                    scriptHostArguments = string.Format("/c \"{0}\"", _scriptFilePath);
                    await ExecuteScriptAsync("cmd", scriptHostArguments, parameters);
                    break;
                case ScriptType.Python:
                    scriptHostArguments = string.Format("\"{0}\"", _scriptFilePath);
                    await ExecuteScriptAsync("python.exe", scriptHostArguments, parameters);
                    break;
                case ScriptType.PHP:
                    scriptHostArguments = string.Format("\"{0}\"", _scriptFilePath);
                    await ExecuteScriptAsync("php.exe", scriptHostArguments, parameters);
                    break;
                case ScriptType.Bash:
                    scriptHostArguments = string.Format("\"{0}\"", _scriptFilePath);
                    string bashPath = ResolveBashPath();
                    await ExecuteScriptAsync(bashPath, scriptHostArguments, parameters);
                    break;
                case ScriptType.FSharp:
                    scriptHostArguments = string.Format("\"{0}\"", _scriptFilePath);
                    string fsiPath = ResolveFSharpPath();
                    await ExecuteScriptAsync(fsiPath, scriptHostArguments, parameters);
                    break;
            }
        }

        internal async Task ExecuteScriptAsync(string path, string arguments, object[] invocationParameters)
        {
            object input = invocationParameters[0];
            TraceWriter traceWriter = (TraceWriter)invocationParameters[1];
            IBinderEx binder = (IBinderEx)invocationParameters[2];
            ExecutionContext functionExecutionContext = (ExecutionContext)invocationParameters[3];
            string invocationId = functionExecutionContext.InvocationId.ToString();

            FunctionStartedEvent startedEvent = new FunctionStartedEvent(functionExecutionContext.InvocationId, Metadata);
            _metrics.BeginEvent(startedEvent);

            // perform any required input conversions
            object convertedInput = input;
            if (input != null)
            {
                HttpRequestMessage request = input as HttpRequestMessage;
                if (request != null)
                {
                    // TODO: Handle other content types? (E.g. byte[])
                    if (request.Content != null && request.Content.Headers.ContentLength > 0)
                    {
                        convertedInput = ((HttpRequestMessage)input).Content.ReadAsStringAsync().Result;
                    }
                }
            }

            TraceWriter.Verbose(string.Format("Function started (Id={0})", invocationId));

            string workingDirectory = Path.GetDirectoryName(_scriptFilePath);
            string functionInstanceOutputPath = Path.Combine(Path.GetTempPath(), "Functions", "Binding", invocationId);

            Dictionary<string, string> environmentVariables = new Dictionary<string, string>();
            InitializeEnvironmentVariables(environmentVariables, functionInstanceOutputPath, input, _outputBindings, functionExecutionContext);

            Dictionary<string, string> bindingData = GetBindingData(convertedInput, binder);
            bindingData["InvocationId"] = invocationId;

            await ProcessInputBindingsAsync(convertedInput, functionInstanceOutputPath, binder, bindingData, environmentVariables);

            // TODO
            // - put a timeout on how long we wait?
            // - need to periodically flush the standard out to the TraceWriter
            Process process = CreateProcess(path, workingDirectory, arguments, environmentVariables);
            process.Start();
            process.WaitForExit();

            bool failed = process.ExitCode != 0;
            startedEvent.Success = !failed;
            _metrics.EndEvent(startedEvent);

            if (failed)
            {
                startedEvent.Success = false;

                TraceWriter.Verbose(string.Format("Function completed (Failure, Id={0})", invocationId));

                string error = process.StandardError.ReadToEnd();
                throw new ApplicationException(error);
            }

            string output = process.StandardOutput.ReadToEnd();
            TraceWriter.Verbose(output);
            traceWriter.Verbose(output);

            await ProcessOutputBindingsAsync(functionInstanceOutputPath, _outputBindings, input, binder, bindingData);

            TraceWriter.Verbose(string.Format("Function completed (Success, Id={0})", invocationId));
        }

        private void InitializeEnvironmentVariables(Dictionary<string, string> environmentVariables, string functionInstanceOutputPath, object input, Collection<FunctionBinding> outputBindings, ExecutionContext context)
        {
            environmentVariables["InvocationId"] = context.InvocationId.ToString();

            foreach (var outputBinding in _outputBindings)
            {
                environmentVariables[outputBinding.Metadata.Name] = Path.Combine(functionInstanceOutputPath, outputBinding.Metadata.Name);
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

        private async Task ProcessInputBindingsAsync(object input, string functionInstanceOutputPath, IBinderEx binder, Dictionary<string, string> bindingData, Dictionary<string, string> environmentVariables)
        {
            // if there are any input or output bindings declared, set up the temporary
            // output directory
            if (_outputBindings.Count > 0 || _inputBindings.Any())
            {
                Directory.CreateDirectory(functionInstanceOutputPath);
            }

            // process input bindings
            foreach (var inputBinding in _inputBindings)
            {
                string filePath = System.IO.Path.Combine(functionInstanceOutputPath, inputBinding.Metadata.Name);
                using (FileStream stream = File.OpenWrite(filePath))
                {
                    // If this is the trigger input, write it directly to the stream.
                    // The trigger binding is a special case because it is early bound
                    // rather than late bound as is the case with all the other input
                    // bindings.
                    if (inputBinding.Metadata.IsTrigger)
                    {
                        if (input is string)
                        {
                            using (StreamWriter sw = new StreamWriter(stream))
                            {
                                await sw.WriteAsync((string)input);
                            }
                        }
                        else if (input is byte[])
                        {
                            byte[] bytes = input as byte[];
                            await stream.WriteAsync(bytes, 0, bytes.Length);
                        }
                        else if (input is Stream)
                        {
                            Stream inputStream = input as Stream;
                            await inputStream.CopyToAsync(stream);
                        }
                    }
                    else
                    {
                        // invoke the input binding
                        BindingContext bindingContext = new BindingContext
                        {
                            Binder = binder,
                            BindingData = bindingData,
                            Value = stream
                        };
                        await inputBinding.BindAsync(bindingContext);
                    }
                }

                environmentVariables[inputBinding.Metadata.Name] = Path.Combine(functionInstanceOutputPath, inputBinding.Metadata.Name);
            }
        }

        private static async Task ProcessOutputBindingsAsync(string functionInstanceOutputPath, Collection<FunctionBinding> outputBindings,
            object input, IBinderEx binder, Dictionary<string, string> bindingData)
        {
            if (outputBindings == null)
            {
                return;
            }

            try
            {
                foreach (var outputBinding in outputBindings)
                {
                    string filePath = System.IO.Path.Combine(functionInstanceOutputPath, outputBinding.Metadata.Name);
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
            // first see if the path is specified as an environment variable
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

        internal static string ResolveFSharpPath()
        {
            // first see if the path is specified as an environment variable
            // (useful for running locally outside of Azure)
            string path = Environment.GetEnvironmentVariable(FsiPathEnvironmentKey);
            if (!string.IsNullOrEmpty(path))
            {
                path = Path.Combine(path, "fsi.exe");
                if (File.Exists(path))
                {
                    return path;
                }
            }

            string relativePath = Path.Combine(@"Microsoft SDKs", "F#", "4.0", "Framework", "v4.0", "fsi.exe");
            return ResolveRelativePathToProgramFiles(relativePath, relativePath, "fsi.exe");
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
