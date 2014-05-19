using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
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
        public SelfWatch(BindResult[] binds, ParameterInfo[] ps, CloudBlockBlob blobResults, TextWriter consoleOutput)
        {
            _command = new SelfWatchCommand(binds, ps, blobResults, consoleOutput);
            _timer = new IntervalSeparationTimer(_command);
            _timer.Start(executeFirst: false);
        }

        // May update the object with a Selfwatch wrapper.
        static ISelfWatch GetWatcher(BindResult bind, ParameterInfo targetParameter)
        {
            return GetWatcher(bind, targetParameter.ParameterType);
        }

        public static ISelfWatch GetWatcher(BindResult bind, Type targetType)
        {
            ISelfWatch watch = bind.Watcher;
            if (watch != null)
            {
                // If explicitly provided, use that.
                return watch;
            }

            watch = bind.Result as ISelfWatch;
            if (watch != null)
            {
                return watch;
            }

            // See if we can apply a watcher on the result
            var t = IsIEnumerableT(targetType);
            if (t != null)
            {
                var tWatcher = typeof(WatchableEnumerable<>).MakeGenericType(t);
                var result = Activator.CreateInstance(tWatcher, bind.Result);

                bind.Result = result; // Update to watchable version.
                return result as ISelfWatch;
            }

            // Nope, 
            return null;
        }

        // Get the T from an IEnumerable<T>. 
        internal static Type IsIEnumerableT(Type typeTarget)
        {
            if (typeTarget.IsGenericType)
            {
                var t2 = typeTarget.GetGenericTypeDefinition();
                if (t2 == typeof(IEnumerable<>))
                {
                    // RowAs<T> doesn't take System.Type, so need to use some reflection. 
                    var rowType = typeTarget.GetGenericArguments()[0];
                    return rowType;
                }
            }
            return null;
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
            public SelfWatchCommand(BindResult[] binds, ParameterInfo[] ps, CloudBlockBlob blobResults, TextWriter consoleOutput)
            {
                _currentDelay = _intialDelay;
                _blobResults = blobResults;
                _consoleOutput = consoleOutput;

                int len = binds.Length;
                ISelfWatch[] watches = new ISelfWatch[len];
                for (int i = 0; i < len; i++)
                {
                    watches[i] = GetWatcher(binds[i], ps[i]);
                }

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
                    RunnerProgram.WriteExceptionChain(e, _consoleOutput);
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
