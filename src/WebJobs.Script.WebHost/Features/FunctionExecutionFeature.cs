// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Features
{
    internal class FunctionExecutionFeature : IFunctionExecutionFeature
    {
        private readonly IScriptJobHost _host;
        private readonly FunctionDescriptor _descriptor;
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;

        public FunctionExecutionFeature(IScriptJobHost host, FunctionDescriptor descriptor, IEnvironment environment, ILoggerFactory loggerFactory)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _descriptor = descriptor;
            _environment = environment;
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostMetrics);
        }

        public bool CanExecute => _descriptor != null;

        public FunctionDescriptor Descriptor => _descriptor;

        public async Task ExecuteAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            if (!CanExecute)
            {
                throw new InvalidOperationException("Unable to execute function without a target.");
            }

            JObject coldStartData = null;
            if (request.IsColdStart())
            {
                coldStartData = new JObject
                {
                    { "requestId", request.GetRequestId() },
                    { "language", Descriptor.Metadata.Language },
                    { "sku", _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku) }
                };

                var dispatchStopwatch = request.GetItemOrDefault<Stopwatch>(ScriptConstants.AzureFunctionsColdStartKey);
                if (dispatchStopwatch != null)
                {
                    dispatchStopwatch.Stop();
                    coldStartData.Add("dispatchDuration", dispatchStopwatch.ElapsedMilliseconds);
                }
            }

            var functionStopwatch = new Stopwatch();
            functionStopwatch.Start();
            var arguments = GetFunctionArguments(_descriptor, request);
            await _host.CallAsync(_descriptor.Name, arguments, cancellationToken);
            functionStopwatch.Stop();

            if (coldStartData != null)
            {
                coldStartData.Add("functionDuration", functionStopwatch.ElapsedMilliseconds);

                var logData = new Dictionary<string, object>
                {
                    [ScriptConstants.LogPropertyEventNameKey] = ScriptConstants.ColdStartEventName,
                    [ScriptConstants.LogPropertyActivityIdKey] = request.GetRequestId()
                };
                _logger.Log(LogLevel.Information, 0, logData, null, (s, e) => coldStartData.ToString(Formatting.None));
            }
        }

        private static Dictionary<string, object> GetFunctionArguments(FunctionDescriptor function, HttpRequest request)
        {
            ParameterDescriptor triggerParameter = function.Parameters.First(p => p.IsTrigger);
            Dictionary<string, object> arguments = new Dictionary<string, object>();

            arguments.Add(triggerParameter.Name, request);

            return arguments;
        }
    }
}
