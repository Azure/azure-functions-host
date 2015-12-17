// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    internal class FunctionStatusLogger : IFunctionInstanceLogger
    {
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IStorageBlobClient _blobClient;
        private readonly IStorageBlobContainer _hostContainer;

        public FunctionStatusLogger(IHostIdProvider hostIdProvider, IStorageBlobClient blobClient)
        {
            if (hostIdProvider == null)
            {
                throw new ArgumentNullException("hostIdProvider");
            }
            if (blobClient == null)
            {
                throw new ArgumentNullException("blobClient");
            }

            _hostIdProvider = hostIdProvider;
            _blobClient = blobClient;
            _hostContainer = _blobClient.GetContainerReference(HostContainerNames.Hosts);
        }

        public async Task<string> LogFunctionStartedAsync(FunctionStartedMessage message, CancellationToken cancellationToken)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            if (message.Reason == ExecutionReason.Portal)
            {
                FunctionStatusMessage statusMessage = CreateFunctionStatusMessage(message);
                await LogFunctionStatusAsync(statusMessage, cancellationToken);
            }

            return null;
        }

        public async Task LogFunctionCompletedAsync(FunctionCompletedMessage message, CancellationToken cancellationToken)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            if (message.Reason == ExecutionReason.Portal)
            {
                FunctionStatusMessage statusMessage = CreateFunctionStatusMessage(message);
                await LogFunctionStatusAsync(statusMessage, cancellationToken);
            }
        }

        public Task DeleteLogFunctionStartedAsync(string startedMessageId, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        internal static FunctionStatusMessage CreateFunctionStatusMessage(FunctionStartedMessage message)
        {
            return new FunctionStatusMessage()
            {
                FunctionId = message.Function.Id,
                FunctionInstanceId = message.FunctionInstanceId,
                Status = "Started",
                StartTime = message.StartTime,
                OutputBlob = message.OutputBlob,
                ParameterLogBlob = message.ParameterLogBlob
            };
        }

        internal static FunctionStatusMessage CreateFunctionStatusMessage(FunctionCompletedMessage message)
        {
            return new FunctionStatusMessage()
            {
                FunctionId = message.Function.Id,
                FunctionInstanceId = message.FunctionInstanceId,
                Status = "Completed",
                StartTime = message.StartTime,
                EndTime = message.EndTime,
                Failure = message.Failure,
                OutputBlob = message.OutputBlob,
                ParameterLogBlob = message.ParameterLogBlob
            };
        }

        private async Task LogFunctionStatusAsync(FunctionStatusMessage message, CancellationToken cancellationToken)
        {
            string hostId = await _hostIdProvider.GetHostIdAsync(cancellationToken);
            var blob = _hostContainer.GetBlockBlobReference(string.Format("invocations/{0}/{1}/{2}", hostId, message.FunctionId, message.FunctionInstanceId));
            string body = JsonConvert.SerializeObject(message, JsonSerialization.Settings);

            await blob.UploadTextAsync(body, cancellationToken: cancellationToken);
        }
    }
}
