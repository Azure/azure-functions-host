// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    // TODO: make this internal
    [CLSCompliant(false)]
    public class ScriptFunctionInvoker : ScriptFunctionInvokerBase
    {
        private const string FsiPathEnvironmentKey = "AzureWebJobs_FsiPath";
        private const string BashPathEnvironmentKey = "AzureWebJobs_BashPath";
        private const string ProgramFiles64bitKey = "ProgramW6432";
        private static ScriptType[] _supportedScriptTypes = new ScriptType[] { ScriptType.WindowsBatch, ScriptType.Python, ScriptType.PHP, ScriptType.Bash };
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
            Binder binder = (Binder)invocationParameters[2];
            ExecutionContext functionExecutionContext = (ExecutionContext)invocationParameters[3];
            string invocationId = functionExecutionContext.InvocationId.ToString();

            FunctionStartedEvent startedEvent = new FunctionStartedEvent(functionExecutionContext.InvocationId, Metadata);
            _metrics.BeginEvent(startedEvent);

            try
            {
                TraceWriter.Info(string.Format("Function started (Id={0})", invocationId));

                string workingDirectory = Path.GetDirectoryName(_scriptFilePath);
                string functionInstanceOutputPath = Path.Combine(Path.GetTempPath(), "Functions", "Binding", invocationId);

                Dictionary<string, string> environmentVariables = new Dictionary<string, string>();
                InitializeEnvironmentVariables(environmentVariables, functionInstanceOutputPath, input, _outputBindings, functionExecutionContext);

                object convertedInput = ConvertInput(input);
                ApplyBindingData(convertedInput, binder);
                Dictionary<string, object> bindingData = binder.BindingData;
                bindingData["InvocationId"] = invocationId;

                await ProcessInputBindingsAsync(convertedInput, functionInstanceOutputPath, binder, _inputBindings, _outputBindings, bindingData, environmentVariables);

                // TODO
                // - put a timeout on how long we wait?
                // - need to periodically flush the standard out to the TraceWriter
                Process process = CreateProcess(path, workingDirectory, arguments, environmentVariables);
                process.Start();
                process.WaitForExit();

                string output = process.StandardOutput.ReadToEnd();
                TraceWriter.Info(output);
                traceWriter.Info(output);

                startedEvent.Success = process.ExitCode == 0;

                if (!startedEvent.Success)
                {
                    string error = process.StandardError.ReadToEnd();
                    throw new ApplicationException(error);
                }

                await ProcessOutputBindingsAsync(functionInstanceOutputPath, _outputBindings, input, binder, bindingData);

                TraceWriter.Info(string.Format("Function completed (Success, Id={0})", invocationId));
            }
            catch
            {
                startedEvent.Success = false;
                TraceWriter.Error(string.Format("Function completed (Failure, Id={0})", invocationId));
                throw;
            }
            finally
            {
                _metrics.EndEvent(startedEvent);
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
