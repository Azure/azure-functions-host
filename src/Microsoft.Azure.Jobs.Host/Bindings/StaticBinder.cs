using System;
using System.Linq;
using System.Reflection;

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
                Assembly hostAssembly = typeof(BlobInputAttribute).Assembly;
                if (attrAssembly != hostAssembly)
                {
                    // Throw an explicit error on mismatch.
                    // Else, we'd just fail the bindings with no error, and none of the methods would get indexed.
                    throw new InvalidOperationException("User application has a different instance of Microsoft.Azure.Jobs.dll than the host app.");
                }
            }

            var types = new[] { typeof(StaticBinder), ServiceBusExtensionTypeLoader.Get("Microsoft.Azure.Jobs.ServiceBusStaticBinder") };
            MethodInfo method = (from t in types
                where t != null
                select t.GetMethod("Bind",
                    BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new Type[]
                    {
                        attr.GetType(), typeof (ParameterInfo)
                    }, null)).FirstOrDefault(m => m!=null);

            if (method == null)
            {
                // Not a binding attribute.
                return null;
            }

            try
            {
                var result = (ParameterStaticBinding)method.Invoke(this, new object[] { attr, parameter });
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

            // Treat ByRef as output. 
            // - for blob listening: if it were input, this would cause a cycle (since we write to the input)
            // - it's output since we're writing to it. So we do need to stamp it with a function guid.
            bool isInput = !isRefKeyword;

            var path = new CloudBlobPath(Resolve(attr.BlobPath));
            return new BlobParameterStaticBinding
            {
                Path = path,
                IsInput = isInput
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

        ParameterStaticBinding Bind(QueueOutputAttribute attr, ParameterInfo parameter)
        {
            string queueName = Resolve(attr.QueueName);
            if (queueName == null)
            {
                queueName = parameter.Name;
            }

            return new QueueParameterStaticBinding
            {
                QueueName = queueName,
                IsInput = false
            };
        }

        ParameterStaticBinding Bind(QueueInputAttribute attr, ParameterInfo parameter)
        {
            string queueName = Resolve(attr.QueueName);
            if (queueName == null)
            {
                queueName = parameter.Name;
            }

            string[] namedParams = QueueInputParameterRuntimeBinding.GetRouteParametersFromParamType(parameter.ParameterType);
            
            return new QueueParameterStaticBinding
            {
                QueueName = queueName,
                IsInput = true,
                Params = namedParams
            };
        }
    }
}
