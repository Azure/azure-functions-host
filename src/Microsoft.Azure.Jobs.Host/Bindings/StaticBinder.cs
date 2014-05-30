using System;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.Jobs.Host;

namespace Microsoft.Azure.Jobs
{
    // Bind from Attributes to ParameterStaticBinding.
    internal class StaticBinder
    {
        private readonly INameResolver _resolver;

        public StaticBinder(INameResolver resolver)
        {
            _resolver = resolver;
        }

        private string Resolve(string input)
        {
            if (_resolver == null)
            {
                return input;
            }

            return _resolver.ResolveWholeString(input);
        }

        public ParameterStaticBinding DoStaticBind(Attribute attr, ParameterInfo parameter)
        {
            // This assumes that we have a single instance of Microsoft.Azure.Jobs.dll between the user's assembly and this host process.
            Assembly attrAssembly = attr.GetType().Assembly;
            string pathUserAttribute = attrAssembly.Location;
            string shortName = System.IO.Path.GetFileName(pathUserAttribute);
            if (String.Equals(shortName, Indexer.AzureJobsFileName, StringComparison.OrdinalIgnoreCase))
            {
                Assembly hostAssembly = typeof(QueueTriggerAttribute).Assembly;
                if (attrAssembly != hostAssembly)
                {
                    // Throw an explicit error on mismatch.
                    // Else, we'd just fail the bindings with no error, and none of the methods would get indexed.
                    throw new InvalidOperationException("User application has a different instance of Microsoft.Azure.Jobs.dll than the host app.");
                }
            }

            MethodInfo method = typeof(StaticBinder).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance,
                null, new Type[] { attr.GetType(), typeof(ParameterInfo) }, null);

            if (method == null)
            {
                // Not a binding attribute.
                return null;
            }

            try
            {
                var result = (ParameterStaticBinding)method.Invoke(
                    method.IsStatic ? null : this, 
                    new object[] { attr, parameter });
                result.Name = parameter.Name;
                return result;
            }
            catch (TargetInvocationException e)
            {
                // Unwrap, especially since callers may catch by type.
                throw e.InnerException;
            }
        }

        private ParameterStaticBinding Bind(BlobInputAttribute attr, ParameterInfo parameter)
        {
            var isRefKeyword = Utility.IsRefKeyword(parameter);
            if (isRefKeyword)
            {
                throw new InvalidOperationException("Input blob parameter can't have [Ref] keyword.");
            }

            var path = new CloudBlobPath(Resolve(attr.BlobPath));
            return new BlobParameterStaticBinding
            {
                Path = path,
                IsInput = false
            };
        }

        private ParameterStaticBinding Bind(BlobOutputAttribute attr, ParameterInfo parameter)
        {
            var path = new CloudBlobPath(Resolve(attr.BlobPath));
            return new BlobParameterStaticBinding
            {
                Path = path,
                IsInput = false
            };
        }

        private ParameterStaticBinding Bind(TableAttribute attr, ParameterInfo parameter)
        {
            string tableName = attr.TableName ?? parameter.Name;
            tableName = Resolve(tableName);

            bool bindsToEntity = attr.RowKey != null;

            if (!bindsToEntity)
            {
                return new TableParameterStaticBinding
                {
                    TableName = tableName,
                };
            }
            else
            {
                return new TableEntityParameterStaticBinding
                {
                    TableName = tableName,
                    PartitionKey = attr.PartitionKey,
                    RowKey = attr.RowKey
                };
            }
        }
    }
}
