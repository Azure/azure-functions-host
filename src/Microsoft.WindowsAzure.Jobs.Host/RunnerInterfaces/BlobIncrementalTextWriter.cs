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
        private CloudBlob _blob;
        private StringWriter _inner;
        private TextWriter _syncWrapper;
        private Timer _timer;

        public BlobIncrementalTextWriter(CloudBlob blob, TimeSpan refreshRate)
        {
            _blob = blob;
            _inner = new StringWriter();
            _syncWrapper = TextWriter.Synchronized(_inner);

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

            _timer = new Timer(Callback, null, TimeSpan.FromMinutes(0), refreshRate);
        }

        public TextWriter Writer
        {
            get { return _syncWrapper; }
        }

        private void Callback(object obj)
        {
            // For synchronized text writer, the object is its own lock.
            lock (_syncWrapper)
            {
                string content = _inner.ToString();
                _blob.UploadText(content);
            }
        }

        public void Close()
        {
            _timer.Dispose();
            Callback(null);
        }
    }
}
