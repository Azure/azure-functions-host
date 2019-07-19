// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FunctionMetadataManager : IFunctionMetadataManager
    {
        private readonly Dictionary<string, ICollection<string>> _functionErrors = new Dictionary<string, ICollection<string>>();
        private readonly Lazy<ImmutableArray<FunctionMetadata>> _metadata;
        private readonly IEnumerable<WorkerConfig> _workerConfigs;
        private readonly IOptions<ScriptJobHostOptions> _scriptOptions;
        private readonly ILogger _logger;

        public FunctionMetadataManager(IOptions<ScriptJobHostOptions> scriptOptions, IOptions<LanguageWorkerOptions> workerConfigOptions, ILoggerFactory loggerFactory)
        {
            _scriptOptions = scriptOptions;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
            _metadata = new Lazy<ImmutableArray<FunctionMetadata>>(LoadFunctionMetadata);
            _workerConfigs = workerConfigOptions.Value.WorkerConfigs;
        }

        public ImmutableArray<FunctionMetadata> Functions => _metadata.Value;

        public ImmutableDictionary<string, ImmutableArray<string>> Errors
            => _functionErrors.ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());

        /// <summary>
        /// Read all functions and populate function metadata.
        /// </summary>
        private ImmutableArray<FunctionMetadata> LoadFunctionMetadata()
        {
            _logger.FunctionMetadataManagerLoadingFunctionsMetadata();
            Collection<FunctionMetadata> functionMetadata = ReadFunctionsMetadata();
            _logger.FunctionMetadataManagerFunctionsLoaded(functionMetadata.Count);

            return functionMetadata.ToImmutableArray();
        }

        internal static Collection<FunctionMetadata> ReadFunctionsMetadata(IEnumerable<string> functionDirectories, ICollection<string> functionsWhiteList, IEnumerable<WorkerConfig> workerConfigs,
            ILogger logger, Dictionary<string, ICollection<string>> functionErrors = null, IFileSystem fileSystem = null)
        {
            functionErrors = functionErrors ?? new Dictionary<string, ICollection<string>>();

            var functions = new Collection<FunctionMetadata>();

            if (functionsWhiteList != null)
            {
                logger.LogInformation($"A function whitelist has been specified, excluding all but the following functions: [{string.Join(", ", functionsWhiteList)}]");
            }

            foreach (var scriptDir in functionDirectories)
            {
                var function = ReadFunctionMetadata(scriptDir, functionsWhiteList, workerConfigs, functionErrors, fileSystem);
                if (function != null)
                {
                    functions.Add(function);
                }
            }
            return functions;
        }

        private Collection<FunctionMetadata> ReadFunctionsMetadata()
        {
            _functionErrors.Clear();
            var options = _scriptOptions.Value;
            return ReadFunctionsMetadata(options.RootScriptDirectorySnapshot, options.Functions, _workerConfigs, _logger, _functionErrors);
        }

        internal static FunctionMetadata ReadFunctionMetadata(string scriptDir, ICollection<string> functionsWhiteList, IEnumerable<WorkerConfig> workerConfigs, Dictionary<string, ICollection<string>> functionErrors, IFileSystem fileSystem = null)
        {
            string functionName = null;

            try
            {
                // read the function config
                if (!Utility.TryReadFunctionConfig(scriptDir, out string json, fileSystem))
                {
                    // not a function directory
                    return null;
                }

                functionName = Path.GetFileName(scriptDir);
                if (functionsWhiteList != null &&
                    !functionsWhiteList.Contains(functionName, StringComparer.OrdinalIgnoreCase))
                {
                    // a functions filter has been specified and the current function is
                    // not in the filter list
                    return null;
                }

                ValidateName(functionName);

                JObject functionConfig = JObject.Parse(json);

                if (!TryParseFunctionMetadata(functionName, functionConfig, scriptDir, workerConfigs, out FunctionMetadata functionMetadata, out string functionError, fileSystem))
                {
                    // for functions in error, log the error and don't
                    // add to the functions collection
                    Utility.AddFunctionError(functionErrors, functionName, functionError);
                    return null;
                }
                else if (functionMetadata != null)
                {
                    return functionMetadata;
                }
            }
            catch (Exception ex)
            {
                // log any unhandled exceptions and continue
                Utility.AddFunctionError(functionErrors, functionName, Utility.FlattenException(ex, includeSource: false), isFunctionShortName: true);
            }
            return null;
        }

        internal static void ValidateName(string name, bool isProxy = false)
        {
            if (!Utility.IsValidFunctionName(name))
            {
                throw new InvalidOperationException(string.Format("'{0}' is not a valid {1} name.", name, isProxy ? "proxy" : "function"));
            }
        }

        internal static bool TryParseFunctionMetadata(string functionName, JObject functionConfig, string scriptDirectory, IEnumerable<WorkerConfig> workerConfigs,
              out FunctionMetadata functionMetadata, out string error, IFileSystem fileSystem = null)
        {
            fileSystem = fileSystem ?? new FileSystem();

            error = null;
            functionMetadata = ParseFunctionMetadata(functionName, functionConfig, scriptDirectory);

            try
            {
                functionMetadata.ScriptFile = DeterminePrimaryScriptFile(functionConfig, scriptDirectory, fileSystem);
            }
            catch (FunctionConfigurationException exc)
            {
                error = exc.Message;
                return false;
            }
            functionMetadata.Language = ParseLanguage(functionMetadata.ScriptFile, workerConfigs);
            functionMetadata.EntryPoint = (string)functionConfig["entryPoint"];

            return true;
        }

        private static FunctionMetadata ParseFunctionMetadata(string functionName, JObject configMetadata, string scriptDirectory)
        {
            var functionMetadata = new FunctionMetadata
            {
                Name = functionName,
                FunctionDirectory = scriptDirectory
            };

            JArray bindingArray = (JArray)configMetadata["bindings"];
            if (bindingArray == null || bindingArray.Count == 0)
            {
                throw new FormatException("At least one binding must be declared.");
            }

            if (bindingArray != null)
            {
                foreach (JObject binding in bindingArray)
                {
                    BindingMetadata bindingMetadata = BindingMetadata.Create(binding);
                    functionMetadata.Bindings.Add(bindingMetadata);
                }
            }

            JToken isDirect;
            if (configMetadata.TryGetValue("configurationSource", StringComparison.OrdinalIgnoreCase, out isDirect))
            {
                var isDirectValue = isDirect.ToString();
                if (string.Equals(isDirectValue, "attributes", StringComparison.OrdinalIgnoreCase))
                {
                    functionMetadata.IsDirect = true;
                }
                else if (!string.Equals(isDirectValue, "config", StringComparison.OrdinalIgnoreCase))
                {
                    throw new FormatException($"Illegal value '{isDirectValue}' for 'configurationSource' property in {functionMetadata.Name}'.");
                }
            }

            return functionMetadata;
        }

        /// <summary>
        /// Determines which script should be considered the "primary" entry point script.
        /// </summary>
        /// <exception cref="ConfigurationErrorsException">Thrown if the function metadata points to an invalid script file, or no script files are present.</exception>
        internal static string DeterminePrimaryScriptFile(JObject functionConfig, string scriptDirectory, IFileSystem fileSystem = null)
        {
            fileSystem = fileSystem ?? new FileSystem();

            // First see if there is an explicit primary file indicated
            // in config. If so use that.
            string functionPrimary = null;
            string scriptFile = (string)functionConfig["scriptFile"];

            if (!string.IsNullOrEmpty(scriptFile))
            {
                string scriptPath = fileSystem.Path.Combine(scriptDirectory, scriptFile);
                if (!fileSystem.File.Exists(scriptPath))
                {
                    throw new FunctionConfigurationException("Invalid script file name configuration. The 'scriptFile' property is set to a file that does not exist.");
                }

                functionPrimary = scriptPath;
            }
            else
            {
                string[] functionFiles = fileSystem.Directory.EnumerateFiles(scriptDirectory)
                    .Where(p => fileSystem.Path.GetFileName(p).ToLowerInvariant() != ScriptConstants.FunctionMetadataFileName)
                    .ToArray();

                if (functionFiles.Length == 0)
                {
                    throw new FunctionConfigurationException("No function script files present.");
                }

                if (functionFiles.Length == 1)
                {
                    // if there is only a single file, that file is primary
                    functionPrimary = functionFiles[0];
                }
                else
                {
                    // if there is a "run" file, that file is primary,
                    // for Node, any index.js file is primary
                    functionPrimary = functionFiles.FirstOrDefault(p =>
                        fileSystem.Path.GetFileNameWithoutExtension(p).ToLowerInvariant() == "run" ||
                        fileSystem.Path.GetFileName(p).ToLowerInvariant() == "index.js");
                }
            }

            if (string.IsNullOrEmpty(functionPrimary))
            {
                throw new FunctionConfigurationException("Unable to determine the primary function script. Try renaming your entry point script to 'run' (or 'index' in the case of Node), " +
                    "or alternatively you can specify the name of the entry point script explicitly by adding a 'scriptFile' property to your function metadata.");
            }

            return Path.GetFullPath(functionPrimary);
        }

        internal static string ParseLanguage(string scriptFilePath, IEnumerable<WorkerConfig> workerConfigs)
        {
            // determine the script type based on the primary script file extension
            string extension = Path.GetExtension(scriptFilePath).ToLowerInvariant().TrimStart('.');
            switch (extension)
            {
                case "csx":
                case "cs":
                    return DotNetScriptTypes.CSharp;
                case "dll":
                    return DotNetScriptTypes.DotNetAssembly;
            }
            var workerConfig = workerConfigs.FirstOrDefault(config => config.Extensions.Contains("." + extension));
            if (workerConfig != null)
            {
                return workerConfig.Language;
            }
            return null;
        }
    }
}
