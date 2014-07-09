using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs
{
    internal class ValueWatcher
    {
        private readonly IIntervalSeparationCommand _command;
        private readonly IntervalSeparationTimer _timer;

        public void Stop()
        {
            _timer.Stop();

            // Flush remaining. do this after timer has been shutdown to avoid races. 
            _command.Execute();
        }

        // Begin watchers.
        public ValueWatcher(IReadOnlyDictionary<string, IWatcher> watches, CloudBlockBlob blobResults, TextWriter consoleOutput)
        {
            _command = new ValueWatcherCommand(watches, blobResults, consoleOutput);
            _timer = new IntervalSeparationTimer(_command);
            _timer.Start(executeFirst: false);
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

        private class ValueWatcherCommand : IIntervalSeparationCommand
        {
            private readonly TimeSpan _intialDelay = TimeSpan.FromSeconds(3); // Wait before first Log, small for initial quick log
            private readonly TimeSpan _refreshRate = TimeSpan.FromSeconds(10);  // Wait inbetween logs
            private readonly IReadOnlyDictionary<string, IWatcher> _watches;
            private readonly CloudBlockBlob _blobResults;
            private readonly TextWriter _consoleOutput;

            private TimeSpan _currentDelay;
            private string _lastContent;

            public ValueWatcherCommand(IReadOnlyDictionary<string, IWatcher> watches, CloudBlockBlob blobResults, TextWriter consoleOutput)
            {
                _currentDelay = _intialDelay;
                _blobResults = blobResults;
                _consoleOutput = consoleOutput;
                _watches = watches;
            }

            public TimeSpan SeparationInterval
            {
                get { return _currentDelay; }
            }

            public void Execute()
            {
                LogStatusWorker();
                _currentDelay = _refreshRate;
            }

            private void LogStatusWorker()
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
                    _blobResults.UploadText(content);
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
        }
    }
}
