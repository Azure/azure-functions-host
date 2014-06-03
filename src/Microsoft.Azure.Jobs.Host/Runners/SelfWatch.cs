using System;
using System.IO;
using System.Text;
using Microsoft.Azure.Jobs.Internals;
using Microsoft.WindowsAzure.Storage.Blob;

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
        // May update args array with selfwatch wrappers.
        public SelfWatch(ISelfWatch[] watches, CloudBlockBlob blobResults, TextWriter consoleOutput)
        {
            _command = new SelfWatchCommand(watches, blobResults, consoleOutput);
            _timer = new IntervalSeparationTimer(_command);
            _timer.Start(executeFirst: false);
        }

        private class SelfWatchCommand : IIntervalSeparationCommand
        {
            private readonly TimeSpan _intialDelay = TimeSpan.FromSeconds(3); // Wait before first Log, small for initial quick log
            private readonly TimeSpan _refreshRate = TimeSpan.FromSeconds(10);  // Wait inbetween logs
            private readonly ISelfWatch[] _watches;
            private readonly CloudBlockBlob _blobResults;
            private readonly TextWriter _consoleOutput;

            private TimeSpan _currentDelay;
            private string _lastContent;

            // May update args array with selfwatch wrappers.
            public SelfWatchCommand(ISelfWatch[] watches, CloudBlockBlob blobResults, TextWriter consoleOutput)
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
                StringBuilder sb = new StringBuilder();
                foreach (var watch in _watches)
                {
                    if (watch != null)
                    {
                        string val = watch.GetStatus();
                        sb.AppendLine(val);
                    }
                    else
                    {
                        sb.AppendLine(); // blank for a place holder.
                    }
                }
                try
                {
                    string content = sb.ToString();

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
                    WebSitesExecuteFunction.WriteExceptionChain(e, _consoleOutput);
                    _consoleOutput.WriteLine("-------------------------");
                }
            }
        }

        /// <summary>
        /// The protocol between Host and Dashboard for SelfWatch notes demand that newlines are encoded as "; ".
        /// </summary>
        public static string EncodeSelfWatchStatus(string status)
        {
            if (status == null)
            {
                throw new ArgumentNullException("status");
            }
            return status.Replace(Environment.NewLine, "; ");
        }

        /// <summary>
        /// The protocol between Host and Dashboard for SelfWatch notes demand that newlines are encoded as "; ".
        /// </summary>
        public static string DecodeSelfWatchStatus(string status)
        {
            if (status == null)
            {
                throw new ArgumentNullException("status");
            }
            return status.Replace("; ", Environment.NewLine);
        }
    }
}
