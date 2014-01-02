using System;
using System.IO;
using System.Threading;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
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

        public BlobIncrementalTextWriter(CloudBlob blob, TimeSpan refreshRate)
            : this(content => blob.UploadText(content))
        {
            // Prepend existing 
            string existingContent = BlobClient.ReadBlob(blob); // null if no exist            
            if (existingContent != null)
            {
                // This can happen if the function was running previously and the 
                // node crashed. Save previous output, could be useful for diagnostics.
                _inner.WriteLine("Previous execution information:");
                _inner.WriteLine(existingContent);

                var lastTime = BlobClient.GetBlobModifiedUtcTime(blob);
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
    }
}
