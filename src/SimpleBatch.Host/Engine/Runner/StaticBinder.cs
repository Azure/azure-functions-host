using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;




namespace Microsoft.WindowsAzure.Jobs
{
    // Bindings use ParameterInfo for a (Type, Name, IsOut) pair. 
    // provide a non-reflection based implementation. 
    // Reuse ParameterInfo rather than creating another abstraction. 
    internal class FakeParameterInfo : ParameterInfo
    {
        private readonly string _name;
        private readonly Type _type;
        private readonly ParameterAttributes _flags;
        private readonly object[] _attributes;

        public FakeParameterInfo(Type paramType, string name, bool isOut, object[] attributes = null)
        {
            _attributes = attributes ?? new object[0];
            _type = paramType;
            _name = name;

            // Both ref and out keywords are T&.
            // but only out keyword gives ParameterInfo.IsOut = true.
            _flags = isOut ? ParameterAttributes.Out : ParameterAttributes.In;
        }

        public override object[] GetCustomAttributes(bool inherit)
        {
            return _attributes;
        }

        public override ParameterAttributes Attributes
        {
            get
            {
                return _flags;
            }
        }

        public override Type ParameterType
        {
            get
            {
                return _type;
            }
        }

        public override string Name
        {
            get
            {
                if (_name == null)
                {
                    throw new InvalidOperationException("No parameter name is available");
                }
                return _name;
            }
        }
    }

    // Bind from Attributes to ParameterStaticBinding.
    internal class StaticBinder
    {
        public static ParameterStaticBinding DoStaticBind(Attribute attr, ParameterInfo parameter)
        {
            // This assumes that we have a single instance of Microsoft.WindowsAzure.Jobs.dll between the user's assembly and this host process.
            Assembly attrAssembly = attr.GetType().Assembly;
            string pathUserAttribute = attrAssembly.Location;
            string shortName = System.IO.Path.GetFileName(pathUserAttribute);
            if (string.Compare(shortName, Indexer.AzureJobsFileName, StringComparison.OrdinalIgnoreCase) == 0)
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
                filename = string.Format("{0}.config.txt", parameter.Name);
            }
            return new ConfigParameterStaticBinding { Filename = filename };
        }

        private static ParameterStaticBinding Bind(BlobInputAttribute attr, ParameterInfo parameter)
        {
            bool isLease = Utility.IsRefKeyword(parameter);

            // Treat ByRef as output. 
            // - for blob listening: if it were input, this would cause a cycle (since we write to the input)
            // - it's output since we're writing to it. So we do need to stamp it with a function guid.
            bool isInput = !isLease;

            var path = new CloudBlobPath(attr.ContainerName);
            return new BlobParameterStaticBinding
            {
                Path = path,
                IsInput = isInput
            };
        }

        private static ParameterStaticBinding Bind(BlobOutputAttribute attr, ParameterInfo parameter)
        {
            var path = new CloudBlobPath(attr.ContainerName);
            return new BlobParameterStaticBinding
            {
                Path = path,
                IsInput = false
            };
        }

        private static ParameterStaticBinding Bind(TableAttribute attr, ParameterInfo parameter)
        {
            return new TableParameterStaticBinding
            {
                TableName = attr.TableName,
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

            return new QueueParameterStaticBinding
            {
                QueueName = queueName,
                IsInput = true
            };
        }

    }
}
