using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Timers;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    // $$$ Need to wrap all the other methods too?
    // Flush on a timer so that we get updated input. 
    // Flush will come on a different thread, so we need to have thread-safe
    // access between the Reader (ToString)  and the Writers (which are happening as our
    // caller uses the textWriter that we return)
    internal class BlobFunctionOutput : IFunctionOutput
    {
    }
}
