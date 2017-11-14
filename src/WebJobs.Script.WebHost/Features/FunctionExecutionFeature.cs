// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Features
{
    internal class FunctionExecutionFeature : IFunctionExecutionFeature
    {
        private readonly ScriptHost _host;
        private readonly FunctionDescriptor _descriptor;

        /// <summary>
        /// Initializes a new instance of the <see cref="FunctionExecutionFeature"/> class.
        /// </summary>
        /// <param name="host">The instacne of the <see cref="ScriptHost"/> to use for execution.</param>
        /// <param name="descriptor">The target <see cref="FunctionDescriptor"/> or <see cref="null"/> if there is no function target.</param>
        public FunctionExecutionFeature(ScriptHost host, FunctionDescriptor descriptor)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _descriptor = descriptor;
        }

        public bool CanExecute => _descriptor != null;

        public FunctionDescriptor Descriptor => _descriptor;

        public Task ExecuteAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            if (!CanExecute)
            {
                throw new InvalidOperationException("Unable to execute function without a target.");
            }

            Dictionary<string, object> arguments = GetFunctionArguments(_descriptor, request);

            return _host.CallAsync(_descriptor.Name, arguments, cancellationToken);
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
