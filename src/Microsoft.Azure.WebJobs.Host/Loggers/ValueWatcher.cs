// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class ValueWatcher
    {
        private readonly ITaskSeriesCommand _command;
        private readonly ITaskSeriesTimer _timer;

        // Begin watchers.
        public ValueWatcher(IReadOnlyDictionary<string, IWatcher> watches, CloudBlockBlob blobResults,
            TextWriter consoleOutput)
        {
            ValueWatcherCommand command = new ValueWatcherCommand(watches, blobResults, consoleOutput);
            _command = command;
            _timer = ValueWatcherCommand.CreateTimer(command);
            _timer.Start();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _timer.StopAsync(cancellationToken);

            // Flush remaining. do this after timer has been shutdown to avoid races. 
            await _command.ExecuteAsync(cancellationToken);
        }

        public static void AddLogs(IReadOnlyDictionary<string, IWatcher> watches,
            IDictionary<string, ParameterLog> collector)
        {
            foreach (KeyValuePair<string, IWatcher> item in watches)
            {
                IWatcher watch = item.Value;

                if (watch == null)
                {
                    continue;
                }

                ParameterLog status = watch.GetStatus();

                if (status == null)
                {
                    continue;
                }

                collector.Add(item.Key, status);
            }
        }

        private class ValueWatcherCommand : ITaskSeriesCommand
        {
            // Wait before first Log, small for initial quick log
            private static readonly TimeSpan _intialDelay = TimeSpan.FromSeconds(3);
            // Wait in between logs
            private static readonly TimeSpan _refreshRate = TimeSpan.FromSeconds(10);

            private readonly IReadOnlyDictionary<string, IWatcher> _watches;
            private readonly CloudBlockBlob _blobResults;
            private readonly TextWriter _consoleOutput;

            private string _lastContent;

            public ValueWatcherCommand(IReadOnlyDictionary<string, IWatcher> watches, CloudBlockBlob blobResults,
                TextWriter consoleOutput)
            {
                _blobResults = blobResults;
                _consoleOutput = consoleOutput;
                _watches = watches;
            }

            public async Task<TaskSeriesCommandResult> ExecuteAsync(CancellationToken cancellationToken)
            {
                await LogStatusWorkerAsync(cancellationToken);
                return new TaskSeriesCommandResult(wait: Task.Delay(_refreshRate));
            }

            private async Task LogStatusWorkerAsync(CancellationToken cancellationToken)
            {
                if (_blobResults == null)
                {
                    return;
                }

                Dictionary<string, ParameterLog> logs = new Dictionary<string, ParameterLog>();
                AddLogs(_watches, logs);
                string content = JsonConvert.SerializeObject(logs);

                try
                {
                    if (_lastContent == content)
                    {
                        // If it hasn't change, then don't re upload stale content.
                        return;
                    }

                    _lastContent = content;
                    await _blobResults.UploadTextAsync(content, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    // Not fatal if we can't update parameter status. 
                    // But at least log what happened for diagnostics in case it's an infrastructure bug.                 
                    _consoleOutput.WriteLine("---- Parameter status update failed ---");
                    _consoleOutput.WriteLine(e.ToDetails());
                    _consoleOutput.WriteLine("-------------------------");
                }
            }

            public static ITaskSeriesTimer CreateTimer(ValueWatcherCommand command)
            {
                return new TaskSeriesTimer(command, Task.Delay(_intialDelay));
            }
        }
    }
}
