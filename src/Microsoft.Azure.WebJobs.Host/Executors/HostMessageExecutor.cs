// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class HostMessageExecutor : ITriggerExecutor<CloudQueueMessage>
    {
        private readonly IFunctionExecutor _innerExecutor;
        private readonly IFunctionIndexLookup _functionLookup;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;

        public HostMessageExecutor(IFunctionExecutor innerExecutor, IFunctionIndexLookup functionLookup,
            IFunctionInstanceLogger functionInstanceLogger)
        {
            _innerExecutor = innerExecutor;
            _functionLookup = functionLookup;
            _functionInstanceLogger = functionInstanceLogger;
        }

        public async Task<bool> ExecuteAsync(CloudQueueMessage value, CancellationToken cancellationToken)
        {
            HostMessage model = JsonCustom.DeserializeObject<HostMessage>(value.AsString);

            if (model == null)
            {
                throw new InvalidOperationException("Invalid invocation message.");
            }

            CallAndOverrideMessage callAndOverrideModel = model as CallAndOverrideMessage;

            if (callAndOverrideModel != null)
            {
                await ProcessCallAndOverrideMessage(callAndOverrideModel, value.InsertionTime.Value, cancellationToken);
                return true;
            }

            AbortHostInstanceMessage abortModel = model as AbortHostInstanceMessage;

            if (abortModel != null)
            {
                ProcessAbortHostInstanceMessage();
                return true;
            }

            string error = String.Format(CultureInfo.InvariantCulture,
                "Unsupported invocation type '{0}'.", model.Type);
            throw new NotSupportedException(error);
        }

        // This snapshot won't contain full normal data for Function.FullName, Function.ShortName and Function.Parameters.
        // (All we know is an unavailable function ID; which function location method info to use is a mystery.)
        private static FunctionCompletedMessage CreateFailedMessage(CallAndOverrideMessage message, DateTimeOffset insertionType)
        {
            DateTimeOffset startAndEndTime = DateTimeOffset.UtcNow;

            // In theory, we could also set HostId, HostInstanceId and WebJobRunId; we'd just have to expose that data
            // directly to this Worker class.
            return new FunctionCompletedMessage
            {
                FunctionInstanceId = message.Id,
                Function = new FunctionDescriptor
                {
                    Id = message.FunctionId
                },
                Arguments = message.Arguments,
                ParentId = message.ParentId,
                Reason = message.Reason,
                StartTime = startAndEndTime,
                EndTime = startAndEndTime,
                Failure = new FunctionFailure
                {
                    ExceptionType = typeof(InvalidOperationException).FullName,
                    ExceptionDetails = String.Format(CultureInfo.CurrentCulture,
                        "No function '{0}' currently exists.", message.FunctionId)
                }
            };
        }

        private IFunctionInstance CreateFunctionInstance(CallAndOverrideMessage message)
        {
            IFunctionDefinition function = _functionLookup.Lookup(message.FunctionId);

            if (function == null)
            {
                return null;
            }

            IDictionary<string, object> objectParameters = new Dictionary<string, object>();

            if (message.Arguments != null)
            {
                foreach (KeyValuePair<string, string> item in message.Arguments)
                {
                    objectParameters.Add(item.Key, item.Value);
                }
            }

            return function.InstanceFactory.Create(message.Id, message.ParentId, message.Reason, objectParameters);
        }

        private async Task ProcessCallAndOverrideMessage(CallAndOverrideMessage message, DateTimeOffset insertionTime,
            CancellationToken cancellationToken)
        {
            IFunctionInstance instance = CreateFunctionInstance(message);

            if (instance != null)
            {
                await _innerExecutor.TryExecuteAsync(instance, cancellationToken);
            }
            else
            {
                // Log that the function failed.
                FunctionCompletedMessage failedMessage = CreateFailedMessage(message, insertionTime);
                await _functionInstanceLogger.LogFunctionCompletedAsync(failedMessage, cancellationToken);
            }
        }

        private static void ProcessAbortHostInstanceMessage()
        {
            bool terminated = NativeMethods.TerminateProcess(NativeMethods.GetCurrentProcess(), 1);
            Debug.Assert(terminated);
        }
    }
}
