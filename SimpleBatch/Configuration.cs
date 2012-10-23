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
    // Argument can implement this to be self describing. 
    // This can be queried on another thread. 
    public interface ISelfWatch
    {
        string GetStatus();
    }

    public class BindResult
    {
        // The actual object passed into the user's method.
        // This can be updated on function return if the parameter is an out-parameter.
        public object Result { get; set; }

        // A self-watch on the object for providing real-time status information
        // about how that object is being used.
        public virtual ISelfWatch Watcher
        {
            get
            {
                return this.Result as ISelfWatch;
            }
        }

        // A cleanup action called for this parameter after the function returns.
        // Called in both regular and exceptional cases. 
        // This can do things like close a stream, or queue a message.
        public virtual void OnPostAction()
        {
            // default is nop
        }
    }

    // ### Pass this in. 
    public interface IBinder
    {
        BindResult<T> Bind<T>(Attribute a);
        string AccountConnectionString { get; }
    }

    public static class IBinderExtensions
    {
        // Get a stream for the given blob. The storage account is relative to binder.AccountConnetionString,
        // and the container and blob name are specified.
        public static BindResult<T> BindReadStream<T>(this IBinder binder, string containerName, string blobName)
        {
            return binder.Bind<T>(new BlobInputAttribute(Path.Combine(containerName, blobName)));
        }

        public static BindResult<T> BindWriteStream<T>(this IBinder binder, string containerName, string blobName)
        {
            return binder.Bind<T>(new BlobOutputAttribute(Path.Combine(containerName, blobName)));
        }
    }

    // Strongly typed wrapper around a result.
    // This is useful in model binders.
    // ### Call cleanup function? And how does that interfere with out parameter?
    public class BindResult<T> : BindResult
    {
        private readonly BindResult[] _inners;
        public Action<T> Cleanup;

        // this OnPostAction() will chain to inners
        public BindResult(T result, params BindResult[] inners)
        {
            _inners = inners;
            this.Result = result;
        }

        public BindResult(T result)
        {
            this.Result = result;
        }

        public new T Result
        {
            get
            {
                BindResult x = this;
                return (T)x.Result;
            }
            set
            {
                BindResult x = this;
                x.Result = value;
            }
        }

        public override void OnPostAction()
        {
            if (Cleanup != null)
            {
                Cleanup(this.Result);
            }

            if (_inners != null)
            {
                foreach (var inner in _inners)
                {
                    inner.OnPostAction();
                }
            }
        }
    }

   

    // Note the binders here don't depend on a specific azure client library
    // So use generic things like connection strings instead of CloudStorageAccount classes.
    // This is because this assembly is included by user code, and the user may pick any azure client library.

    // Bind a CloudBlob to something more structured.
    // Use CloudBlob instead of Stream so that we have metadata, filename.
    public interface ICloudBlobBinder
    {
        // Returned object should be assignable to target type.
        BindResult Bind(IBinder binder, string containerName, string blobName, Type targetType);
    }

    public interface ICloudBlobBinderProvider
    {
        // Can this binder read/write the given type?
        // This could be a straight type match, or a generic type.
        ICloudBlobBinder TryGetBinder(Type targetType, bool isInput);
    }

    public interface ICloudTableBinder
    {
        BindResult Bind(IBinder bindingContext, Type targetType, string tableName);
    }
    public interface ICloudTableBinderProvider
    {
        // isReadOnly - True if we know we want it read only (must have been specified in the attribute).
        ICloudTableBinder TryGetBinder(Type targetType, bool isReadOnly);
    }

    // Binds to arbitrary entities in the cloud
    public interface ICloudBinder
    {
        BindResult Bind(IBinder bindingContext, ParameterInfo parameter);
    }

    // Binder for any arbitrary azure things. Could even bind to multiple things. 
    // No meta infromation here, so we can't reason anything about it (Reader, writer, etc)
    public interface ICloudBinderProvider
    {
        ICloudBinder TryGetBinder(Type targetType);
    }

    public interface IConfiguration
    {
        // Could cache a wrapper directly binding against IClourBlobBinder.
        IList<ICloudBlobBinderProvider> BlobBinders { get; }

        IList<ICloudTableBinderProvider> TableBinders { get; }

        // General type binding. 
        IList<ICloudBinderProvider> Binders { get; }
    }
}