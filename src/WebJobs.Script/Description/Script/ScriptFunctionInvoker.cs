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
using Microsoft.Azure.WebJobs.Script.Binding;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    // TODO: make this internal
    public class ScriptFunctionInvoker : ScriptFunctionInvokerBase
    {
        private const string BashPathEnvironmentKey = "AzureWebJobs_BashPath";
        private const string ProgramFiles64bitKey = "ProgramW6432";
        private static ScriptType[] _supportedScriptTypes = new ScriptType[] { ScriptType.WindowsBatch, ScriptType.Python, ScriptType.PHP, ScriptType.Bash };
        private readonly string _scriptFilePath;
        private static ScriptHost _host;

        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;

        internal ScriptFunctionInvoker(string scriptFilePath, ScriptHost host, FunctionMetadata functionMetadata,
            Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
            : base(host, functionMetadata)
        {
            _scriptFilePath = scriptFilePath;
            _host = host;
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
        }

        public static bool IsSupportedScriptType(ScriptType scriptType)
        {
            return _supportedScriptTypes.Contains(scriptType);
        }

        protected override async Task<object> InvokeCore(object[] parameters, FunctionInvocationContext context)
        {
            string scriptHostArguments;
            switch (Metadata.ScriptType)
            {
                case ScriptType.WindowsBatch:
                    scriptHostArguments = string.Format("/c \"{0}\"", _scriptFilePath);
                    await ExecuteScriptAsync("cmd", scriptHostArguments, parameters, context);
                    break;
                case ScriptType.Python:
                    // Passing -u forces stdout to be unbuffered so we can log messages as they happen.
                    scriptHostArguments = string.Format("-u \"{0}\"", _scriptFilePath);
                    await ExecuteScriptAsync("python.exe", scriptHostArguments, parameters, context);
                    break;
                case ScriptType.PHP:
                    scriptHostArguments = string.Format("\"{0}\"", _scriptFilePath);
                    await ExecuteScriptAsync("php.exe", scriptHostArguments, parameters, context);
                    break;
                case ScriptType.Bash:
                    scriptHostArguments = string.Format("\"{0}\"", _scriptFilePath);
                    string bashPath = ResolveBashPath();
                    await ExecuteScriptAsync(bashPath, scriptHostArguments, parameters, context);
                    break;
            }

            return null;
        }

        internal async Task ExecuteScriptAsync(string path, string arguments, object[] invocationParameters, FunctionInvocationContext context)
        {
            object input = invocationParameters[0];
            string invocationId = context.ExecutionContext.InvocationId.ToString();

            string workingDirectory = Path.GetDirectoryName(_scriptFilePath);
            string functionInstanceOutputPath = Path.Combine(Path.GetTempPath(), "Functions", "Binding", invocationId);

            Dictionary<string, string> environmentVariables = new Dictionary<string, string>();
            InitializeEnvironmentVariables(environmentVariables, functionInstanceOutputPath, input, _outputBindings, context.ExecutionContext);

            object convertedInput = ConvertInput(input);
            Utility.ApplyBindingData(convertedInput, context.Binder.BindingData);
            Dictionary<string, object> bindingData = context.Binder.BindingData;
            bindingData["InvocationId"] = invocationId;

            await ProcessInputBindingsAsync(convertedInput, functionInstanceOutputPath, context.Binder, _inputBindings, _outputBindings, bindingData, environmentVariables);

            SetExecutionContextVariables(context.ExecutionContext, environmentVariables);

            Process process = CreateProcess(path, workingDirectory, arguments, environmentVariables);
            var userTraceWriter = context.TraceWriter;
            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    // the user's TraceWriter will automatically log to ILogger as well
                    userTraceWriter.Info(e.Data);
                }
            };

            var tcs = new TaskCompletionSource<object>();
            process.Exited += (sender, args) =>
            {
                tcs.TrySetResult(null);
            };

            process.Start();
            process.BeginOutputReadLine();

            await tcs.Task;

            if (process.ExitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();
                throw new ApplicationException(error);
            }

            await ProcessOutputBindingsAsync(functionInstanceOutputPath, _outputBindings, input, context.Binder, bindingData);
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
                StartInfo = psi,
                EnableRaisingEvents = true
            };
        }

        internal static string ResolveBashPath()
        {
            // first see if the path is specified as an environment variable
            // (useful for running locally outside of Azure)
            string path = _host.SettingsManager.GetSetting(BashPathEnvironmentKey);
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
                programFiles = _host.SettingsManager.GetSetting(ProgramFiles64bitKey) ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
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
