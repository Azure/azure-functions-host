using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs
{
    internal class SelfWatch
    {
        private readonly IIntervalSeparationCommand _command;
        private readonly IntervalSeparationTimer _timer;

        public void Stop()
        {
            _timer.Stop();

            // Flush remaining. do this after timer has been shutdown to avoid races. 
            _command.Execute();
        }

        // Begin self-watches.
        public SelfWatch(IReadOnlyDictionary<string, ISelfWatch> watches, CloudBlockBlob blobResults, TextWriter consoleOutput)
        {
            _command = new SelfWatchCommand(watches, blobResults, consoleOutput);
            _timer = new IntervalSeparationTimer(_command);
            _timer.Start(executeFirst: false);
        }

        public static void AddLogs(IReadOnlyDictionary<string, ISelfWatch> watches,
            IDictionary<string, ParameterLog> collector)
        {
            foreach (KeyValuePair<string, ISelfWatch> item in watches)
            {
                ISelfWatch watch = item.Value;

                if (watch == null)
                {
                    continue;
                }

                string status = watch.GetStatus();

                if (status == null)
                {
                    continue;
                }

                collector.Add(item.Key, new StringParameterLog { Value = status });
            }
        }

        private class SelfWatchCommand : IIntervalSeparationCommand
        {
            private readonly TimeSpan _intialDelay = TimeSpan.FromSeconds(3); // Wait before first Log, small for initial quick log
            private readonly TimeSpan _refreshRate = TimeSpan.FromSeconds(10);  // Wait inbetween logs
            private readonly IReadOnlyDictionary<string, ISelfWatch> _watches;
            private readonly CloudBlockBlob _blobResults;
            private readonly TextWriter _consoleOutput;

            private TimeSpan _currentDelay;
            private string _lastContent;

            // May update args array with selfwatch wrappers.
            public SelfWatchCommand(IReadOnlyDictionary<string, ISelfWatch> watches, CloudBlockBlob blobResults, TextWriter consoleOutput)
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
                LogSelfWatchWorker();
                _currentDelay = _refreshRate;
            }

            private void LogSelfWatchWorker()
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
                    // Not fatal if we can't update selfwatch. 
                    // But at least log what happened for diagnostics in case it's an infrastructure bug.                 
                    _consoleOutput.WriteLine("---- SelfWatch failed ---");
                    _consoleOutput.WriteLine(e.ToDetails());
                    _consoleOutput.WriteLine("-------------------------");
                }
            }
        }
    }
}
