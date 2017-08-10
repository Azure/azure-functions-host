// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Rpc;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ChannelContext
    {
        public IObservable<StreamingMessage> InputStream { get; set; }

        public IObserver<StreamingMessage> OutputStream { get; set; }

        public string RequestId { get; set; }

        public string WorkerId { get; set; }

        public async Task<FunctionLoadResponse> LoadAsync(FunctionLoadRequest request)
        {
            var result = await SendAsync(new StreamingMessage
            {
                FunctionLoadRequest = request
            });
            return result.FunctionLoadResponse;
        }

        public async Task<InvocationResponse> InvokeAsync(InvocationRequest request)
        {
            // handle cancellation
            var response = (await SendAsync(new StreamingMessage
            {
                InvocationRequest = request
            }, TimeSpan.FromMinutes(5))).InvocationResponse;

            response.Result.VerifySuccess();
            return response;
        }

        private Task<StreamingMessage> SendAsync(StreamingMessage request, TimeSpan? timeout = null)
        {
            if (string.IsNullOrEmpty(request.RequestId))
            {
                request.RequestId = Guid.NewGuid().ToString();
            }

            // send streaming message
            OutputStream.OnNext(request);

            TaskCompletionSource<StreamingMessage> responseSource = new TaskCompletionSource<StreamingMessage>();

            IDisposable subscription = null;

            // create request subscription
            // TODO: timeouts
            subscription = InputStream
                .Where(msg => msg.RequestId == request.RequestId)
                .Timeout(timeout ?? TimeSpan.FromSeconds(10))
                .Subscribe(response =>
                {
                    responseSource.SetResult(response);
                    subscription?.Dispose();
                }, err =>
                {
                    responseSource.SetException(err);
                    subscription?.Dispose();
                });

            return responseSource.Task;
        }
    }
}
