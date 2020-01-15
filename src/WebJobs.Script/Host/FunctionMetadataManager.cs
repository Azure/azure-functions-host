// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FunctionMetadataManager : IFunctionMetadataManager
    {
        private const string _functionConfigurationErrorMessage = "Unable to determine the primary function script.Make sure atleast one script file is present.Try renaming your entry point script to 'run' or alternatively you can specify the name of the entry point script explicitly by adding a 'scriptFile' property to your function metadata.";
        private readonly bool _isHttpWorker;
        private readonly Lazy<ImmutableArray<FunctionMetadata>> _functionMetadataArray;
        private readonly IOptions<ScriptJobHostOptions> _scriptOptions;
        private readonly IFunctionMetadataProvider _functionMetadataProvider;
        private readonly ILogger _logger;
        private Dictionary<string, ICollection<string>> _functionErrors = new Dictionary<string, ICollection<string>>();

        public FunctionMetadataManager(IOptions<ScriptJobHostOptions> scriptOptions, IFunctionMetadataProvider functionMetadataProvider, IOptions<HttpWorkerOptions> httpWorkerOptions, ILoggerFactory loggerFactory)
        {
            _scriptOptions = scriptOptions;
            _functionMetadataProvider = functionMetadataProvider;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
            _functionMetadataArray = new Lazy<ImmutableArray<FunctionMetadata>>(LoadFunctionMetadata);
            _isHttpWorker = httpWorkerOptions.Value.Description != null;
        }

        public ImmutableArray<FunctionMetadata> Functions => _functionMetadataArray.Value;

        public ImmutableDictionary<string, ImmutableArray<string>> Errors { get; private set; }

        /// <summary>
        /// Read all functions and populate function metadata.
        /// </summary>
        internal ImmutableArray<FunctionMetadata> LoadFunctionMetadata()
        {
            ICollection<string> functionsWhiteList = _scriptOptions.Value.Functions;
            _logger.FunctionMetadataManagerLoadingFunctionsMetadata();

            List<FunctionMetadata> functionMetadataList = _functionMetadataProvider.GetFunctionMetadata().ToList();
            _functionErrors = _functionMetadataProvider.FunctionErrors.ToDictionary(kvp => kvp.Key, kvp => (ICollection<string>)kvp.Value.ToList());

            // Validate
            foreach (FunctionMetadata functionMetadata in functionMetadataList.ToList())
            {
                if (!IsScriptFileDetermined(functionMetadata))
                {
                    // Exclude invalid functions
                    functionMetadataList.Remove(functionMetadata);
                }
            }
            Errors = _functionErrors.ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());

            if (functionsWhiteList != null)
            {
                _logger.LogInformation($"A function whitelist has been specified, excluding all but the following functions: [{string.Join(", ", functionsWhiteList)}]");
                functionMetadataList = functionMetadataList.Where(function => functionsWhiteList.Any(functionName => functionName.Equals(function.Name, StringComparison.CurrentCultureIgnoreCase))).ToList();
                Errors = _functionErrors.Where(kvp => functionsWhiteList.Any(functionName => functionName.Equals(kvp.Key, StringComparison.CurrentCultureIgnoreCase))).ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());
            }
            _logger.FunctionMetadataManagerFunctionsLoaded(functionMetadataList.Count());

            return functionMetadataList.ToImmutableArray();
        }

        internal bool IsScriptFileDetermined(FunctionMetadata functionMetadata)
        {
            try
            {
                if (string.IsNullOrEmpty(functionMetadata.ScriptFile) && !_isHttpWorker)
                {
                    throw new FunctionConfigurationException(_functionConfigurationErrorMessage);
                }
            }
            catch (FunctionConfigurationException exc)
            {
                // for functions in error, log the error and don't
                // add to the functions collection
                Utility.AddFunctionError(_functionErrors, functionMetadata.Name, exc.Message);
                return false;
            }
            return true;
        }
    }
}