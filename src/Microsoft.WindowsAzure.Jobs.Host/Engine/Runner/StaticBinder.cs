using System;
using System.Globalization;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    // Bind from Attributes to ParameterStaticBinding.
    internal class StaticBinder
    {
        public static ParameterStaticBinding DoStaticBind(Attribute attr, ParameterInfo parameter)
        {
            // This assumes that we have a single instance of Microsoft.WindowsAzure.Jobs.dll between the user's assembly and this host process.
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
                    throw new InvalidOperationException("User application has a different instance of Microsoft.WindowsAzure.Jobs.dll than the host app.");
                }
            }

            var t = typeof(StaticBinder);
            MethodInfo method = t.GetMethod("Bind",
                BindingFlags.NonPublic | BindingFlags.Static, null,
                new Type[] { 
                attr.GetType(), typeof(ParameterInfo)
            }, null);

            if (method == null)
            {
                // Not a binding attribute.
                return null;
            }

            try
            {
                var result = (ParameterStaticBinding)method.Invoke(null, new object[] { attr, parameter });
                result.Name = parameter.Name;
                return result;
            }
            catch (TargetInvocationException e)
            {
                // Unwrap, especially since callers may catch by type.
                throw e.InnerException;
            }
        }

        private static ParameterStaticBinding Bind(ConfigAttribute attr, ParameterInfo parameter)
        {
            string filename = attr.Filename;
            if (string.IsNullOrEmpty(filename))
            {
                // $$$ Settle on convention 
                // Don't conflict with app.config filenames either. 
                filename = String.Format(CultureInfo.InvariantCulture, "{0}.config.txt", parameter.Name);
            }
            return new ConfigParameterStaticBinding { Filename = filename };
        }

        private static ParameterStaticBinding Bind(BlobInputAttribute attr, ParameterInfo parameter)
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

            var path = new CloudBlobPath(attr.BlobPath);
            return new BlobParameterStaticBinding
            {
                Path = path,
                IsInput = isInput
            };
        }

        private static ParameterStaticBinding Bind(BlobOutputAttribute attr, ParameterInfo parameter)
        {
            var path = new CloudBlobPath(attr.BlobPath);
            return new BlobParameterStaticBinding
            {
                Path = path,
                IsInput = false
            };
        }

        private static ParameterStaticBinding Bind(TableAttribute attr, ParameterInfo parameter)
        {
            string tableName = attr.TableName ?? parameter.Name;
            
            return new TableParameterStaticBinding
            {
                TableName = tableName,
            };
        }

        private static ParameterStaticBinding Bind(BlobInputsAttribute attr, ParameterInfo parameter)
        {
            var path = new CloudBlobPath(attr.BlobPathPattern);

            return new BlobAggregateParameterStaticBinding
            {
                BlobPathPattern = new CloudBlobPath(attr.BlobPathPattern),
            };
        }

        static ParameterStaticBinding Bind(QueueOutputAttribute attr, ParameterInfo parameter)
        {
            string queueName = attr.QueueName;
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

        static ParameterStaticBinding Bind(QueueInputAttribute attr, ParameterInfo parameter)
        {
            string queueName = attr.QueueName;
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
