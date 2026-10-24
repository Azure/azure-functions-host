// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy.Logging;

/// <summary>
/// Queues worker system-log records and writes them to standard output from a background task.
/// </summary>
internal sealed partial class WorkerSystemLogWriter : BackgroundService, IWorkerSystemLogWriter
{
    private readonly Channel<string> _records;
    private readonly TextWriter _writer;
    private readonly TimeSpan _shutdownDrainTimeout;
    private readonly ILogger<WorkerSystemLogWriter> _logger;
    private Exception? _pipelineFailure;
    private int _stopping;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerSystemLogWriter"/> class.
    /// </summary>
    public WorkerSystemLogWriter(
        IOptions<WorkerProxyOptions> options,
        ILogger<WorkerSystemLogWriter> logger)
        : this(
            Console.Out,
            options?.Value ?? throw new ArgumentNullException(nameof(options)),
            logger)
    {
    }

    internal WorkerSystemLogWriter(
        TextWriter writer,
        WorkerProxyOptions options,
        ILogger<WorkerSystemLogWriter> logger)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _shutdownDrainTimeout = options.SystemLogShutdownDrainTimeout;
        _records = Channel.CreateBounded<string>(new BoundedChannelOptions(options.SystemLogQueueCapacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    public WorkerSystemLogEnqueueResult TryEnqueue(string record)
    {
        ArgumentException.ThrowIfNullOrEmpty(record);

        if (Volatile.Read(ref _stopping) != 0)
        {
            return WorkerSystemLogEnqueueResult.PipelineStopping;
        }

        if (Volatile.Read(ref _pipelineFailure) is not null)
        {
            return WorkerSystemLogEnqueueResult.PipelineFaulted;
        }

        if (_records.Writer.TryWrite(record))
        {
            return WorkerSystemLogEnqueueResult.Accepted;
        }

        if (Volatile.Read(ref _pipelineFailure) is not null)
        {
            return WorkerSystemLogEnqueueResult.PipelineFaulted;
        }

        return Volatile.Read(ref _stopping) != 0
            ? WorkerSystemLogEnqueueResult.PipelineStopping
            : WorkerSystemLogEnqueueResult.QueueFull;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (string record in _records.Reader.ReadAllAsync())
            {
                await _writer.WriteLineAsync(record);
                await _writer.FlushAsync();
            }
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _pipelineFailure, exception);
            _records.Writer.TryComplete(exception);
            Log.PipelineFaulted(_logger, exception);
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _stopping, 1);
        _records.Writer.TryComplete();

        using CancellationTokenSource drainSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        drainSource.CancelAfter(_shutdownDrainTimeout);

        try
        {
            await base.StopAsync(drainSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Log.DrainTimedOut(_logger, _shutdownDrainTimeout);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Error, "The worker system-log stdout pipeline faulted.")]
        public static partial void PipelineFaulted(ILogger logger, Exception exception);

        [LoggerMessage(1, LogLevel.Warning, "The worker system-log stdout pipeline did not drain within {DrainTimeout}.")]
        public static partial void DrainTimedOut(ILogger logger, TimeSpan drainTimeout);
    }
}
