// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc;

public abstract partial class WorkerChannel
{
    private const string RequestLifetimeTrackingProperty = "WorkerChannel.RequestLifetimeTracking";

    /// <summary>
    /// Gets the number of invocations tracked by this channel, including any explicitly tracked request lifetimes.
    /// </summary>
    protected int TrackedInvocationCount => _executingInvocations.Count;

    /// <summary>
    /// Gets a value indicating whether this channel has any tracked invocations.
    /// </summary>
    protected bool HasTrackedInvocations => !_executingInvocations.IsEmpty;

    /// <summary>
    /// Posts an invocation, optionally tracking it from admission through HTTP response completion.
    /// </summary>
    /// <param name="context">The invocation to enqueue.</param>
    /// <param name="trackRequestLifetime">Whether to retain the invocation through its full request lifetime.</param>
    protected void PostInvocation(ScriptInvocationContext context, bool trackRequestLifetime)
    {
        if (!FunctionInputBuffers.TryGetValue(context.FunctionMetadata.GetFunctionId(), out BufferBlock<ScriptInvocationContext> buffer))
        {
            throw new InvalidOperationException($"Function '{context.FunctionMetadata.Name}' is not loaded by worker '{Id}'.");
        }

        string invocationId = context.ExecutionContext.InvocationId.ToString();
        ExecutingInvocation invocation = null;
        if (trackRequestLifetime)
        {
            Task responseCompleted = Task.CompletedTask;
            if (context.Inputs?.Select(input => input.Val).OfType<HttpRequest>().FirstOrDefault() is HttpRequest request)
            {
                TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                request.HttpContext.Response.OnCompleted(() =>
                {
                    completion.TrySetResult();
                    return Task.CompletedTask;
                });
                responseCompleted = completion.Task;
            }

            invocation = new(context, _messageDispatcherFactory.Create(invocationId),
                WaitForRequestCompletionAsync(context, responseCompleted));
            if (!_executingInvocations.TryAdd(invocationId, invocation))
            {
                invocation.Dispose();
                throw new InvalidOperationException($"Worker '{Id}' is already tracking invocation '{invocationId}'.");
            }

            context.Properties[RequestLifetimeTrackingProperty] = true;
            OnInvocationActivityChanged();
        }

        if (!buffer.Post(context))
        {
            if (invocation is not null)
            {
                CompleteExecutingInvocation(invocationId, invocation);
            }

            throw new InvalidOperationException($"Worker '{Id}' rejected the invocation input.");
        }

        if (invocation is not null)
        {
            _ = CompleteRequestInvocationAsync(invocationId, invocation);
        }
    }

    /// <summary>
    /// Notifies a derived channel after a tracked request lifetime is added or removed.
    /// </summary>
    protected virtual void OnInvocationActivityChanged()
    {
    }

    private async Task WaitForRequestCompletionAsync(ScriptInvocationContext context, Task responseCompleted)
    {
        try
        {
            await context.ResultSource.Task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The invoker owns result failures; request accounting must still wait for forwarding.
            _workerChannelLogger.LogDebug(exception, "Invocation on worker {WorkerId} ended unsuccessfully.", Id);
        }

        if (context.Properties.TryGetValue(ScriptConstants.HttpProxyTask, out object value) && value is Task forwarding)
        {
            try
            {
                await forwarding.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _workerChannelLogger.LogDebug(exception, "HTTP forwarding on worker {WorkerId} ended unsuccessfully.", Id);
            }
        }

        await responseCompleted.ConfigureAwait(false);
    }

    private async Task CompleteRequestInvocationAsync(string invocationId, ExecutingInvocation invocation)
    {
        try
        {
            await invocation.RequestCompletion.ConfigureAwait(false);
        }
        finally
        {
            CompleteExecutingInvocation(invocationId, invocation);
        }
    }

    private bool TryGetInvocationResponse(string invocationId, out ExecutingInvocation invocation)
    {
        if (_executingInvocations.TryGetValue(invocationId, out invocation) && invocation.RequestCompletion is not null)
        {
            return invocation.TryBeginResponse();
        }

        return _executingInvocations.TryRemove(invocationId, out invocation);
    }

    private void CompleteExecutingInvocation(string invocationId, ExecutingInvocation invocation)
    {
        if (_executingInvocations.TryRemove(new KeyValuePair<string, ExecutingInvocation>(invocationId, invocation)))
        {
            try
            {
                invocation.Dispose();
            }
            finally
            {
                if (invocation.RequestCompletion is not null)
                {
                    OnInvocationActivityChanged();
                }
            }
        }
    }

    private sealed class ExecutingInvocation(
        ScriptInvocationContext context, IInvocationMessageDispatcher dispatcher, Task requestCompletion = null) : IDisposable
    {
        private int _responseStarted;
        private int _disposed;

        public ScriptInvocationContext Context { get; } = context;

        public IInvocationMessageDispatcher Dispatcher { get; } = dispatcher;

        public Task RequestCompletion { get; } = requestCompletion;

        public bool IsAwaitingResponse => Volatile.Read(ref _responseStarted) == 0;

        public bool TryBeginResponse() => Interlocked.CompareExchange(ref _responseStarted, 1, 0) == 0;

        public void Dispose()
        {
            Interlocked.Exchange(ref _responseStarted, 1);
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                (Dispatcher as IDisposable)?.Dispose();
            }
        }
    }
}
