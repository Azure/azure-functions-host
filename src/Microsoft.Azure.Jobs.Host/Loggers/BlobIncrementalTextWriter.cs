using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs
{
    // $$$ Need to wrap all the other methods too?
    // Flush on a timer so that we get updated input. 
    // Flush will come on a different thread, so we need to have thread-safe
    // access between the Reader (ToString)  and the Writers (which are happening as our
    // caller uses the textWriter that we return)
    internal class BlobIncrementalTextWriter
    {
        // Called on background timer to flush contents of TextWriter
        private Action<string> _fpFlush;        

        // contents for what's written. Owned by the timer thread.
        private StringWriter _inner;

        // thread-safe access to _inner so that user threads can write to it. 
        private TextWriter _syncWrapper;

        private Timer _timer;

        public BlobIncrementalTextWriter(CloudBlockBlob blob, TimeSpan refreshRate)
            : this(content => blob.UploadText(content))
        {
            // Prepend existing 
            string existingContent = ReadBlob(blob); // null if no exist            
            if (existingContent != null)
            {
                // This can happen if the function was running previously and the 
                // node crashed. Save previous output, could be useful for diagnostics.
                _inner.WriteLine("Previous execution information:");
                _inner.WriteLine(existingContent);

                var lastTime = GetBlobModifiedUtcTime(blob);
                if (lastTime.HasValue)
                {
                    var delta = DateTime.UtcNow - lastTime.Value;
                    _inner.WriteLine("... Last write at {0}, {1} ago", lastTime, delta);
                }

                _inner.WriteLine("========================");
            }

            Start(refreshRate);
        }

        // Doesn't start the timer. 
        internal BlobIncrementalTextWriter(Action<string> fpFlush)
        {
            _fpFlush = fpFlush;
            _inner = new StringWriter();
            _syncWrapper = TextWriter.Synchronized(_inner);            
        }

        internal void Start(TimeSpan refreshRate)
        {
            Callback(null);
            _timer = new Timer(Callback, null, TimeSpan.FromMinutes(0), refreshRate);
        }

        public TextWriter Writer
        {
            get { return _syncWrapper; }
        }

        private void Callback(object obj)
        {
            _syncWrapper.Flush();

            // For synchronized text writer, the object is its own lock.
            lock (_syncWrapper)
            {
                string content = _inner.ToString();
                _fpFlush(content);
            }
        }

        public void Close()
        {
            _timer.Dispose();

            try
            {
                _syncWrapper.Flush();
            }
            catch
            {
            }
            Callback(null);
        }

        private static DateTime? GetBlobModifiedUtcTime(ICloudBlob blob)
        {
            if (!blob.Exists())
            {
                return null; // no blob, no time.
            }

            var props = blob.Properties;
            var time = props.LastModified;
            return time.HasValue ? (DateTime?)time.Value.UtcDateTime : null;
        }

        // Return Null if doesn't exist
        [DebuggerNonUserCode]
        private static string ReadBlob(ICloudBlob blob)
        {
            // Beware! Blob.DownloadText does not strip the BOM! 
            try
            {
                using (var stream = blob.OpenRead())
                using (StreamReader sr = new StreamReader(stream, detectEncodingFromByteOrderMarks: true))
                {
                    string data = sr.ReadToEnd();
                    return data;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
