using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace RunnerInterfaces
{
    // Function location is accessed by pinging a URL. 
    public class UriFunctionLocation : FunctionLocation
    {
        // Uniquely specifies the method.
        public string Uri { get; set; }

        public override string GetShortName()
        {
            // !!! Some convention to shorten?
            return this.Uri;
        }

        public override string GetId()
        {
            return this.Uri;
        }

        // Could implement ReadFile if we had a convention on the URI
    }

    // !!! Can't serialize this. 
    // Function is already loaded into memory. 
    public class MethodInfoFunctionLocation : FunctionLocation
    {
        public MethodInfo MethodInfo { get; set; }

        public override string GetGroupingKey()
        {
            return this.MethodInfo.DeclaringType.Assembly.FullName;
        }

        public override string GetShortName()
        {
            return string.Format("{0}.{1}", MethodInfo.DeclaringType.Name, MethodInfo.Name);
        }

        public override string GetId()
        {
            return string.Format("{0}.{1}", MethodInfo.DeclaringType.FullName, MethodInfo.Name);
        }

        public override string ReadFile(string filename)
        {
            var root = Path.GetDirectoryName(this.MethodInfo.DeclaringType.Assembly.Location);

            string path = Path.Combine(root, filename);
            return File.ReadAllText(path);
        }
    }

    // Function lives on some file-based system. Need to locate (possibly download) the assembly, load the type, and fine the method. 
    public abstract class FileFunctionLocation : FunctionLocation
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
    public class RemoteFunctionLocation : FileFunctionLocation
    {
        // Base class has the account connection string. 
        public string ContainerName { get; set; }
        public string BlobName { get; set; }

        // For convenience, return Account,Container,Blob as a single unit. 
        public CloudBlobDescriptor GetBlob()
        {
            return new CloudBlobDescriptor
            {
                AccountConnectionString = this.AccountConnectionString,
                BlobName = BlobName,
                ContainerName = ContainerName
            };
        }

        public override string GetGroupingKey()
        {
            return this.GetBlob().GetId();
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

        // Assume caller has download the remote location to localCopy
        // The container of the remote loc should be downloaded into the same directory as localCopy
        public LocalFunctionLocation GetAsLocal(string localCopy)
        {
            return new LocalFunctionLocation
            {
                AssemblyPath = localCopy,
                AccountConnectionString = AccountConnectionString,
                MethodName = this.MethodName,
                TypeName = this.TypeName
            };
        }
    }

    // Function lives on a local disk.  
    public class LocalFunctionLocation : FileFunctionLocation
    {
        // Assumes other dependencies are in the same directory. 
        public string AssemblyPath { get; set; }

        public override string GetGroupingKey()
        {
            return this.AssemblyPath;
        }

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
    public abstract class FunctionLocation
    {
        // The account this function is associated with. 
        // This will be used to resolve all static bindings.
        // This is used at runtime bindings. 
        public string AccountConnectionString { get; set; }

        // Uniquely stringize this object. Can be used for equality comparisons. 
        // !!! Is this unique even for different derived types?
        public abstract string GetId();

        // Useful name for human display. This has no uniqueness properties and can't be used as a rowkey. 
        public abstract string GetShortName();

        // For UI purposes, get a key that can be used to group functions from a similar storage location together. 
        // eg, all functions in the same dll. 
        public virtual string GetGroupingKey()
        {
            return this.GetId();
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