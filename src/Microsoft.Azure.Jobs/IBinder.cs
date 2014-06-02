using System;
using System.Threading;

namespace Microsoft.Azure.Jobs
{
    /// <summary>Represents an attribute binder.</summary>
    public interface IBinder
    {
        /// <summary>Gets the token to monitor for cancellation requests.</summary>
        CancellationToken CancellationToken { get; }

        /// <summary>Binds the specified attribute.</summary>
        /// <typeparam name="T">The type to which to bind.</typeparam>
        /// <param name="attribute">The attribute to bind.</param>
        /// <returns>The bound value.</returns>
        T Bind<T>(Attribute attribute);
    }
}
