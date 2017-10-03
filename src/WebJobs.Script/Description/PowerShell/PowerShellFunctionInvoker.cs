// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description.PowerShell;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class PowerShellFunctionInvoker : ScriptFunctionInvokerBase
    {
        private readonly ScriptHost _host;
        private readonly string _scriptFilePath;
        private readonly string _functionName;

        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;

        private string _script;
        private List<string> _moduleFiles;

        internal PowerShellFunctionInvoker(ScriptHost host, FunctionMetadata functionMetadata,
            Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
            : base(host, functionMetadata)
        {
            _host = host;
            _scriptFilePath = functionMetadata.ScriptFile;
            _functionName = functionMetadata.Name;
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
        }

        protected override async Task<object> InvokeCore(object[] parameters, FunctionInvocationContext context)
        {
            object input = parameters[0];
            string invocationId = context.ExecutionContext.InvocationId.ToString();

            object convertedInput = ConvertInput(input);
            Utility.ApplyBindingData(convertedInput, context.Binder.BindingData);
            Dictionary<string, object> bindingData = context.Binder.BindingData;
            bindingData["InvocationId"] = invocationId;

            Dictionary<string, string> environmentVariables = new Dictionary<string, string>();

            string functionInstanceOutputPath = Path.Combine(Path.GetTempPath(), "Functions", "Binding", invocationId);
            await ProcessInputBindingsAsync(convertedInput, functionInstanceOutputPath, context.Binder, _inputBindings, _outputBindings, bindingData, environmentVariables);

            SetExecutionContextVariables(context.ExecutionContext, environmentVariables);

            InitializeEnvironmentVariables(environmentVariables, functionInstanceOutputPath, input, _outputBindings, context.ExecutionContext);

            var userTraceWriter = context.TraceWriter;
            PSDataCollection<ErrorRecord> errors = await InvokePowerShellScript(environmentVariables, userTraceWriter);

            await ProcessOutputBindingsAsync(functionInstanceOutputPath, _outputBindings, input, context.Binder, bindingData);

            ErrorRecord error = errors.FirstOrDefault();
            if (error != null)
            {
                throw new RuntimeException("PowerShell script error", error.Exception, error);
            }
            return null;
        }

        private async Task<PSDataCollection<ErrorRecord>> InvokePowerShellScript(Dictionary<string, string> envVars, TraceWriter traceWriter)
        {
            InitialSessionState iss = InitialSessionState.CreateDefault();
            PSDataCollection<ErrorRecord> errors = new PSDataCollection<ErrorRecord>();

            using (Runspace runspace = RunspaceFactory.CreateRunspace(iss))
            {
                runspace.Open();
                SetRunspaceEnvironmentVariables(runspace, envVars);
                RunspaceInvoke runSpaceInvoker = new RunspaceInvoke(runspace);
                runSpaceInvoker.Invoke("Set-ExecutionPolicy -Scope Process -ExecutionPolicy Unrestricted");

                using (
                    System.Management.Automation.PowerShell powerShellInstance =
                        System.Management.Automation.PowerShell.Create())
                {
                    powerShellInstance.Runspace = runspace;
                    _moduleFiles = GetModuleFilePaths(_host.ScriptConfig.RootScriptPath, _functionName);
                    if (_moduleFiles.Any())
                    {
                        powerShellInstance.AddCommand("Import-Module").AddArgument(_moduleFiles);
                        LogLoadedModules();
                    }

                    _script = GetScript(_scriptFilePath);
                    powerShellInstance.AddScript(_script, true);

                    PSDataCollection<PSObject> outputCollection = new PSDataCollection<PSObject>();
                    outputCollection.DataAdded += (sender, e) => OutputCollectionDataAdded(sender, e, traceWriter);

                    powerShellInstance.Streams.Error.DataAdded += (sender, e) => ErrorDataAdded(sender, e, traceWriter);

                    IAsyncResult result = powerShellInstance.BeginInvoke<PSObject, PSObject>(null, outputCollection);
                    await Task.Factory.FromAsync<PSDataCollection<PSObject>>(result, powerShellInstance.EndInvoke);

                    foreach (ErrorRecord errorRecord in powerShellInstance.Streams.Error)
                    {
                        errors.Add(errorRecord);
                    }
                }

                runspace.Close();
            }

            return errors;
        }

        private void LogLoadedModules()
        {
            List<string> moduleRelativePaths = new List<string>();
            foreach (string moduleFile in _moduleFiles)
            {
                string relativePath = GetRelativePath(_functionName, moduleFile);
                moduleRelativePaths.Add(relativePath);
            }

            if (moduleRelativePaths.Any())
            {
                string message = string.Format("Loaded modules:{0}{1}", Environment.NewLine, string.Join(Environment.NewLine, moduleRelativePaths));
                TraceWriter.Verbose(message);
                Logger?.LogDebug(message);
            }
        }

        internal static string GetRelativePath(string functionName, string moduleFile)
        {
            string pattern = string.Format("^.*?(?=\\\\{0}\\\\)", functionName);
            MatchCollection matchCollection = Regex.Matches(moduleFile, pattern);
            string newtoken = moduleFile.Replace(matchCollection[0].Value, string.Empty);
            string relativePath = newtoken.Replace('\\', '/');
            return relativePath;
        }

        private static void SetRunspaceEnvironmentVariables(Runspace runspace, IDictionary<string, string> envVariables)
        {
            foreach (var pair in envVariables)
            {
                runspace.SessionStateProxy.SetVariable(pair.Key, pair.Value);
            }
        }

        /// <summary>
        /// Event handler for the output stream.
        /// </summary>
        private static void OutputCollectionDataAdded(object sender, DataAddedEventArgs e, TraceWriter traceWriter)
        {
            // trace objects written to the output stream
            var source = (PSDataCollection<PSObject>)sender;
            var data = source[e.Index];
            if (data != null)
            {
                var msg = data.ToString();
                traceWriter.Info(msg);
            }
        }

        /// <summary>
        /// Event handler for the error stream.
        /// </summary>
        private void ErrorDataAdded(object sender, DataAddedEventArgs e, TraceWriter traceWriter)
        {
            var source = (PSDataCollection<ErrorRecord>)sender;
            var msg = GetErrorMessage(_functionName, _scriptFilePath, source[e.Index]);
            traceWriter.Error(msg);
        }

        internal static string GetErrorMessage(string functioName, string scriptFilePath, ErrorRecord errorRecord)
        {
            string fileName = string.IsNullOrEmpty(scriptFilePath) ? string.Empty : Path.GetFileName(scriptFilePath);
            string errorInvocationName = fileName;
            if (errorRecord.InvocationInfo != null)
            {
                errorInvocationName = errorRecord.InvocationInfo.InvocationName;
            }

            string errorStackTrace = string.IsNullOrEmpty(errorRecord.ScriptStackTrace) ? string.Empty : GetStackTrace(functioName, errorRecord.ScriptStackTrace, fileName);

            StringBuilder stringBuilder =
                new StringBuilder(string.Format("{0} : {1}{2}",
                    errorInvocationName,
                    errorRecord, Environment.NewLine));
            stringBuilder.AppendLine(errorStackTrace);
            stringBuilder.AppendLine(string.Format("{0} {1}", PowerShellConstants.AdditionChar, errorInvocationName));
            stringBuilder.AppendLine(string.Format("{0} {1}", PowerShellConstants.AdditionChar,
                new string(PowerShellConstants.UnderscoreChar, errorInvocationName.Length)));
            stringBuilder.AppendLine(string.Format("{0}{1} {2} {3}",
                new string(PowerShellConstants.SpaceChar, PowerShellConstants.SpaceCount),
                PowerShellConstants.AdditionChar, PowerShellConstants.CategoryInfoLabel, errorRecord.CategoryInfo));
            if (string.IsNullOrEmpty(errorInvocationName))
            {
                stringBuilder.AppendLine(string.Format("{0}{1} {2} {3},{4}",
                    new string(PowerShellConstants.SpaceChar, PowerShellConstants.SpaceCount),
                    PowerShellConstants.AdditionChar, PowerShellConstants.FullyQualifiedErrorIdLabel,
                    errorRecord.FullyQualifiedErrorId, fileName));
            }
            else
            {
                stringBuilder.AppendLine(string.Format("{0}{1} {2} {3}",
                    new string(PowerShellConstants.SpaceChar, PowerShellConstants.SpaceCount),
                    PowerShellConstants.AdditionChar, PowerShellConstants.FullyQualifiedErrorIdLabel,
                    errorRecord.FullyQualifiedErrorId));
            }

            return stringBuilder.ToString();
        }

        internal static string GetStackTrace(string functionName, string scriptStackTrace, string fileName)
        {
            string stackTrace = scriptStackTrace.Replace(PowerShellConstants.StackTraceScriptBlock, fileName);

            if (stackTrace.Contains(functionName))
            {
                string[] tokens = stackTrace.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                string[] newtokens = new string[tokens.Length];
                int index = 0;
                foreach (string token in tokens)
                {
                    if (token.Contains(functionName))
                    {
                        string relativePath = GetRelativePath(functionName, token);
                        newtokens[index++] = relativePath;
                    }
                    else if (token.Contains(PowerShellConstants.StackTraceScriptBlock))
                    {
                        newtokens[index++] = token.Replace(PowerShellConstants.StackTraceScriptBlock, fileName);
                    }
                    else
                    {
                        newtokens[index++] = token;
                    }
                }

                stackTrace = string.Join(" ", newtokens);
            }

            return stackTrace;
        }

        internal static string GetScript(string scriptFilePath)
        {
            string script = null;
            if (File.Exists(scriptFilePath))
            {
                script = File.ReadAllText(scriptFilePath);
            }

            return script;
        }

        internal static List<string> GetModuleFilePaths(string rootScriptPath, string functionName)
        {
            List<string> modulePaths = new List<string>();
            string functionFolder = Path.Combine(rootScriptPath, functionName);
            string moduleDirectory = Path.Combine(functionFolder, PowerShellConstants.ModulesFolderName);
            if (Directory.Exists(moduleDirectory))
            {
                modulePaths.AddRange(Directory.GetFiles(moduleDirectory,
                    PowerShellConstants.ModulesManifestFileExtensionPattern,
                    SearchOption.AllDirectories));
                modulePaths.AddRange(Directory.GetFiles(moduleDirectory,
                    PowerShellConstants.ModulesBinaryFileExtensionPattern,
                    SearchOption.AllDirectories));
                modulePaths.AddRange(Directory.GetFiles(moduleDirectory,
                    PowerShellConstants.ModulesScriptFileExtensionPattern,
                    SearchOption.AllDirectories));
            }

            return modulePaths;
        }
    }
}
