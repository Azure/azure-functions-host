using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using SimpleBatch;

namespace SimpleBatch
{
    public interface ICall
    {
        // Queues a call to the given function. Function is resolved against the current "scope". 
        // $$$ Inherits all named args from caller. 
        // Return a tag for logging and stuff?
        Guid InvokeAsync(string functionName, object arguments = null);
    }
}