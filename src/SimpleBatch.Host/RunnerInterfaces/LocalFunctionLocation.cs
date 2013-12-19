using System;
using System.IO;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
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
}
