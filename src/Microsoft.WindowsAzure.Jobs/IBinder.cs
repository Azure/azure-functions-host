using System;
using System.Threading;

namespace Microsoft.WindowsAzure.Jobs
{
    // Public one that we bind to. Simpler, doesn't expose a BindResult. 
    /// <summary>
    /// Represents an attribute binder.
    /// </summary>
    public interface IBinder
    {
        /// <summary>
        /// Gets the Azure Storage connection string.
        /// </summary>
        string AccountConnectionString { get; }

        /// <summary>Gets the token to monitor for cancellation requests.</summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Binds the specified attribute.
        /// </summary>
        /// <typeparam name="T">The type to be bound.</typeparam>
        /// <param name="attribute">The attribute to bind.</param>
        /// <returns>The bound value.</returns>
        T Bind<T>(Attribute attribute);
    }
}
