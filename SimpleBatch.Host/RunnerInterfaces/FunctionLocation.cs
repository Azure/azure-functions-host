using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using System.Runtime.Serialization;

namespace RunnerInterfaces
{
    internal interface IUrlFunctionLocation
    {
        // To invoke, POST to this URL, with FunctionInvokeRequest as the body. 
        string InvokeUrl { get; }
    }

    // Describe a function that lives behind a URL (such as with Kudu). 
    internal class KuduFunctionLocation : FunctionLocation, IUrlFunctionLocation
    {
        // Do a POST to this URI, and pass this instance in the body.  
        public string Uri { get; set; }

        public string AssemblyQualifiedTypeName { get; set; }
        public string MethodName { get; set; }

        // $$$ Needed for ICall support. 
        public override FunctionLocation ResolveFunctionLocation(string methodName)
        {
            return base.ResolveFunctionLocation(methodName);
        }

        public MethodInfoFunctionLocation Convert()
        {
            Type t = Type.GetType(AssemblyQualifiedTypeName);
            MethodInfo m = t.GetMethod(MethodName, BindingFlags.Public | BindingFlags.Static);
            return new MethodInfoFunctionLocation(m)
            {
                AccountConnectionString = this.AccountConnectionString,
            };
        }

        public override string GetId()
        {
            // Need  a URL so that different domains get their own namespace of functions. 
            // But also has to be a valid row/partition key since it's called by FunctionInvokeRequest.ToString()
            string accountName = Utility.GetAccountName(this.AccountConnectionString);
            string shortUrl = new Uri(this.Uri).Host; // gets a human readable (almost) unique name, without invalid chars
            return string.Format(@"kudu\{0}\{1}\{2}", shortUrl, accountName, MethodName);
        }

        public override string GetShortName()
        {
            return MethodName;
        }

        [JsonIgnore]
        public string InvokeUrl
        {
            get
            {
                return Uri;
            }
        }
    }

    // Function is already loaded into memory. 
    internal class MethodInfoFunctionLocation : FunctionLocation
    {
        // $$$ Can't serialize this because it has a real methodinfo. We shouldn't be serializing these anyways. 
        // instead:
        // 1. caller should be converting location into a serializable type and running o
        // 2. entire scenario is already in-memory. 
        [JsonIgnore]
        public MethodInfo MethodInfo { get; set; }

        public string ShortName { get; set; }
        public string Id { get; set; }

        // For deserialization
        public string AssemblyQualifiedTypeName { get; set; }
        public string MethodName { get; set; }

        public MethodInfoFunctionLocation() // For Serialization
        {
        }

        public MethodInfoFunctionLocation(MethodInfo method)
        {
            MethodInfo = method;
            ShortName = string.Format("{0}.{1}", MethodInfo.DeclaringType.Name, MethodInfo.Name);
            Id = string.Format("{0}.{1}", MethodInfo.DeclaringType.FullName, MethodInfo.Name);

            this.AssemblyQualifiedTypeName = MethodInfo.DeclaringType.AssemblyQualifiedName;
            this.MethodName = MethodInfo.Name;
        }

        [OnDeserialized]
        internal void OnDeserialized(StreamingContext ctx)
        {
            if (this.MethodInfo != null)
            {
                return;
            }

            if (AssemblyQualifiedTypeName != null && MethodName != null)
            {
                Type t = Type.GetType(AssemblyQualifiedTypeName);
                if (t != null)
                {
                    MethodInfo m = t.GetMethod(MethodName, BindingFlags.Public | BindingFlags.Static);

                    this.MethodInfo = m;
                }
            }
        }

        public override string GetShortName()
        {
            return ShortName;
        }

        public override string GetId()
        {
            return Id;
        }

        public override string ReadFile(string filename)
        {
            var root = Path.GetDirectoryName(this.MethodInfo.DeclaringType.Assembly.Location);

            string path = Path.Combine(root, filename);
            return File.ReadAllText(path);
        }
    }

    // Function lives on some file-based system. Need to locate (possibly download) the assembly, load the type, and fine the method. 
    internal abstract class FileFunctionLocation : FunctionLocation
    {
        // Entry point into the function 
        // TypeName is relative to Blob
        public string TypeName { get; set; }
        public string MethodName { get; set; }

        public override string GetShortName()
        {
            return this.TypeName + "." + this.MethodName;
        }
    }

    // Function lives on some blob. 
    // Download this to convert to a LocalFunctionLocation
    internal class RemoteFunctionLocation : FileFunctionLocation
    {
        // Base class has the account connection string. 
        public CloudBlobPath DownloadSource { get; set; }

        // For convenience, return Account,Container,Blob as a single unit. 
        public CloudBlobDescriptor GetBlob()
        {
            return new CloudBlobDescriptor
            {
                AccountConnectionString = this.AccountConnectionString,
                BlobName = this.DownloadSource.BlobName,
                ContainerName = this.DownloadSource.ContainerName
            };
        }

        public override string GetId()
        {
            return string.Format(@"{0}\{1}\{2}", GetBlob().GetId(), TypeName, MethodName);
        }

        // Read a file from the function's location. 
        public override string ReadFile(string filename)
        {
            var container = this.GetBlob().GetContainer();
            var blob = container.GetBlobReference(filename);
            string content = Utility.ReadBlob(blob);

            return content;
        }

        // Assume caller has download the remote location to localDirectoryCopy
        // The container of the remote loc should be downloaded into the same directory as localCopy
        public LocalFunctionLocation GetAsLocal(string localDirectoryCopy)
        {
            string assemblyEntryPoint = Path.Combine(localDirectoryCopy, this.DownloadSource.BlobName);

            return new LocalFunctionLocation
            {
                DownloadSource = this.DownloadSource,
                AssemblyPath = assemblyEntryPoint,
                AccountConnectionString = AccountConnectionString,
                MethodName = this.MethodName,
                TypeName = this.TypeName
            };
        }
    }

    // Function lives on a local disk.  
    internal class LocalFunctionLocation : FileFunctionLocation
    {
        // Assumes other dependencies are in the same directory. 
        public string AssemblyPath { get; set; }

        // Where was this downloaded from?
        // Knowing this is essential if a local execution wants to queue up additional calls on the server. 
        public CloudBlobPath DownloadSource { get; set; }

        // ShortName is the method relative to this type. 
        // $$$ Should this return a Local or Remote? 
        public override FunctionLocation ResolveFunctionLocation(string methodName)
        {
            return new RemoteFunctionLocation
            {
                AccountConnectionString = this.AccountConnectionString,
                DownloadSource = DownloadSource,
                MethodName = methodName, // 
                TypeName = this.TypeName
            };
        }

        // $$$ How consistent should this be with a the RemoteFunctionLocation that this was downloaded from?
        public override string GetId()
        {
            return string.Format(@"{0}\{1}\{2}", AssemblyPath, TypeName, MethodName);
        }

        public override string ReadFile(string filename)
        {
            var root = Path.GetDirectoryName(AssemblyPath);

            string path = Path.Combine(root, filename);
            return File.ReadAllText(path);
        }

        public MethodInfo GetLocalMethod()
        {
            Assembly a = Assembly.LoadFrom(this.AssemblyPath);
            return GetLocalMethod(a);
        }

        // Useful if we need to control exactly how AssemblyPath is loaded. 
        public MethodInfo GetLocalMethod(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException("assembly");
            }

            Type t = assembly.GetType(this.TypeName);
            if (t == null)
            {
                throw new InvalidOperationException(string.Format("Type '{0}' does not exist.", this.TypeName));
            }

            MethodInfo m = t.GetMethod(this.MethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null)
            {
                throw new InvalidOperationException(string.Format("Method '{0}' does not exist.", this.MethodName));
            }
            return m;
        }


    }

    // Describes a function somewhere on the cloud
    // This serves as a primary key. 
    // $$$ generalize this. Don't necessarily need the account information 
    // This is static. Still needs args to get invoked. 
    // This can be serialized to JSON. 
    internal abstract class FunctionLocation
    {
        // The account this function is associated with. 
        // This will be used to resolve all static bindings.
        // This is used at runtime bindings. 
        public string AccountConnectionString { get; set; }

        // Uniquely stringize this object. Can be used for equality comparisons. 
        // $$$ Is this unique even for different derived types? 
        // $$$ This vs. ToString?
        public abstract string GetId();

        // Useful name for human display. This has no uniqueness properties and can't be used as a rowkey. 
        public abstract string GetShortName();

        // This is used for ICall. Convert from a short name to another FunctionLocation.
        // $$$ Reconcile methodName with GetShortName (which includes a type name)
        // Should x.ResolveFunctionLocation(x.GetShortName()).Equals(x) == true?
        // Any other invariants here?
        public virtual FunctionLocation ResolveFunctionLocation(string methodName)
        {
            throw new InvalidOperationException("Can't resolve function location for: " + methodName);
        }

        // ToString can be used as an azure row key.
        public override string ToString()
        {
            return Utility.GetAsTableKey(this.GetId());
        }

        public override bool Equals(object obj)
        {
            FunctionLocation other = obj as FunctionLocation;
            if (other == null)
            {
                return false;
            }

            return this.GetId() == other.GetId();
        }

        public override int GetHashCode()
        {
            return this.GetId().GetHashCode();
        }
        
        // Read a file from the function's location. 
        // $$$ Should this be byte[] instead? It's string because we expect caller will deserialize with JSON.
        public virtual string ReadFile(string filename)
        {
            return null; // not available.
        }
    }
}