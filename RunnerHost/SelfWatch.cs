using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using RunnerInterfaces;
using SimpleBatch;
using SimpleBatch.Client;

namespace RunnerHost
{
    public class SelfWatch
    {
        TimeSpan _intialDelay = TimeSpan.FromSeconds(3); // Wait before first Log, small for initial quick log
        TimeSpan _refreshRate = TimeSpan.FromSeconds(10);  // Wait inbetween logs
        

        volatile bool _exitThread;

        string _lastContent; // 

        ISelfWatch[] _watches;
        CloudBlob _blobResults;

        // single Background thread that polls and does logging. 
        // Whereas timers can fire on many different threads. 
        Thread _thread;

        void ThreadCallback(object state)
        {
            Thread.Sleep(_intialDelay);

            while (!_exitThread)
            {
                LogSelfWatchWorker();
                Thread.Sleep(_refreshRate);
            }
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
            catch
            {
                // Not fatal if we can't update selfwatch. 
                // Could happen because we're calling on a timer, and so it 
                // could get invoked concurrently on multiple threads, which 
                // could contend over writing.
            }
        }

        // Called at end to shutdown thread and ensure that we log final results. 
        public void Stop()
        {
            _exitThread = true;
            _thread.Abort();

            // If join fails, then background thread may be in an unknown state. 
            // That's ok. We'll still make a best effort to log. 
            _thread.Join(TimeSpan.FromSeconds(5));
            
            // Flush remaining. do this after timer has been shutdown to avoid races. 
            LogSelfWatchWorker();
        }

        // Begin self-watches. Return a cleanup delegate for stopping the watches. 
        // May update args array with selfwatch wrappers.
        public SelfWatch(BindResult[] binds, ParameterInfo[] ps, CloudBlob blobResults)
        {
            _blobResults = blobResults;

            int len = binds.Length;
            ISelfWatch[] watches = new ISelfWatch[len];
            for (int i = 0; i < len; i++)
            {
                watches[i] = GetWatcher(binds[i], ps[i]);
            }

            _watches = watches;

            // Ensure we only have 1 background thread doing the watches.
            _thread = new Thread(ThreadCallback);
            _thread.Start();            
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
    }
}
