// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Linq;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FunctionMetadataManager : IFunctionMetadataManager
    {
        private readonly Lazy<ImmutableArray<FunctionMetadata>> _metadata;
        private readonly IOptions<ScriptJobHostOptions> _scriptOptions;
        private readonly IFunctionMetadataProvider _functionMetadataProvider;
        private readonly ILogger _logger;

        public FunctionMetadataManager(IOptions<ScriptJobHostOptions> scriptOptions, IFunctionMetadataProvider functionMetadataProvider, ILoggerFactory loggerFactory)
        {
            _scriptOptions = scriptOptions;
            _functionMetadataProvider = functionMetadataProvider;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
            _metadata = new Lazy<ImmutableArray<FunctionMetadata>>(LoadFunctionMetadata);
        }

        public ImmutableArray<FunctionMetadata> Functions => _metadata.Value;

        public ImmutableDictionary<string, ImmutableArray<string>> Errors { get; private set; }

        /// <summary>
        /// Read all functions and populate function metadata.
        /// </summary>
        private ImmutableArray<FunctionMetadata> LoadFunctionMetadata()
        {
            ICollection<string> functionsWhiteList = _scriptOptions.Value.Functions;
            _logger.FunctionMetadataManagerLoadingFunctionsMetadata();
            ImmutableArray<FunctionMetadata> metadata = _functionMetadataProvider.GetFunctionMetadata();
            Errors = _functionMetadataProvider.FunctionErrors;

            if (functionsWhiteList != null)
            {
                _logger.LogInformation($"A function whitelist has been specified, excluding all but the following functions: [{string.Join(", ", functionsWhiteList)}]");
                metadata = metadata.Where(function => functionsWhiteList.Any(functionName => functionName.Equals(function.Name, StringComparison.CurrentCultureIgnoreCase))).ToImmutableArray();
                Errors = _functionMetadataProvider.FunctionErrors.Where(kvp => functionsWhiteList.Any(functionName => functionName.Equals(kvp.Key, StringComparison.CurrentCultureIgnoreCase))).ToImmutableDictionary<string, ImmutableArray<string>>();
            }
            _logger.FunctionMetadataManagerFunctionsLoaded(metadata.Length);
            return metadata;
        }
    }
}