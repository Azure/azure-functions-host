using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Data.Services.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using RunnerInterfaces;
using SimpleBatch;

namespace Orchestrator
{
    // Get a static parameter binding from an parameter.
    static class FlowBinder
    {
        static Dictionary<string, Func<CustomAttributeData, ParameterInfo, ParameterStaticBinding>> _map;

        static void InitMap()
        {
            _map = new Dictionary<string, Func<CustomAttributeData, ParameterInfo, ParameterStaticBinding>>();

            _map.Add(typeof(BlobInputAttribute).FullName, BindBlobInput);
            _map.Add(typeof(BlobOutputAttribute).FullName, BindBlobOutput);
            _map.Add(typeof(TableAttribute).FullName, BindTable);
            _map.Add(typeof(QueueInputAttribute).FullName, BindQueueInput);
            _map.Add(typeof(QueueOutputAttribute).FullName, BindQueueOutput);
            _map.Add(typeof(BlobInputsAttribute).FullName, BindBlobAggregateInput);
        }

        public static ParameterStaticBinding Bind(ParameterInfo p)
        {
            var attrs = p.GetCustomAttributesData();

            // $$$ Errors if we can't bind a type?

            foreach (CustomAttributeData attrData in attrs)
            {
                var flow = Bind(attrData, p);
                if (flow != null)
                {
                    // $$$ Check for multiple conflicting attributes and fail?
                    return flow;
                }
            } // foreach attr
            return null;
        }

        private static ParameterStaticBinding Bind(CustomAttributeData attrData, ParameterInfo p)
        {
            if (_map == null)
            {
                InitMap();
            }
            
            string name = attrData.Constructor.DeclaringType.FullName;

            Func<CustomAttributeData, ParameterInfo, ParameterStaticBinding> func;
            if (_map.TryGetValue(name, out func))
            {
                var result = func(attrData, p);
                result.Name = p.Name;
                return result;
            }
            return null;            
        }

        static ParameterStaticBinding BindBlobInput(CustomAttributeData attrData, ParameterInfo p)
        {
            var blobInputAttr = BlobInputAttribute.Build(attrData);

            var path = new CloudBlobPath(blobInputAttr.ContainerName);
            return new BlobParameterStaticBinding
            {
                Path = path,
                IsInput = true
            };
        }

        static ParameterStaticBinding BindBlobOutput(CustomAttributeData attrData, ParameterInfo p)
        {
            var blobOutputAttr = BlobOutputAttribute.Build(attrData);

            var path = new CloudBlobPath(blobOutputAttr.ContainerName);
            return new BlobParameterStaticBinding
            {
                Path = path,
                IsInput = false
            };
        }


        static ParameterStaticBinding BindTable(CustomAttributeData attrData, ParameterInfo p)
        {
            var tableAttr = TableAttribute.Build(attrData);

            bool isReadOnly = false;
            if (p.ParameterType.FullName == typeof(IAzureTableReader).FullName) // across assemblies
            {
                // Beware, not just that it can read (implements IAzureTableReader), but that 
                // it's readonly (does not implement IAzureTableWriter).
                // Err on the side of safety
                isReadOnly = true;
            }


            return new TableParameterStaticBinding
            {
                TableName = tableAttr.TableName,
                IsReadOnly = isReadOnly                   
            };            
        }

        static ParameterStaticBinding BindBlobAggregateInput(CustomAttributeData attrData, ParameterInfo p)
        {
            var blobInputsAttr = BlobInputsAttribute.Build(attrData);

            var path = new CloudBlobPath(blobInputsAttr.BlobPathPattern);

            return new BlobAggregateParameterStaticBinding
            {
                BlobPathPattern = new CloudBlobPath(blobInputsAttr.BlobPathPattern),
            };
        }

        static ParameterStaticBinding BindQueueOutput(CustomAttributeData attrData, ParameterInfo p)
        {
            QueueOutputAttribute queueOutputAttr = QueueOutputAttribute.Build(attrData);

            string queueName = queueOutputAttr.QueueName;
            if (queueName == null)
            {
                queueName = p.Name;
            }

            return new QueueParameterStaticBinding
            {
                QueueName = queueName,
                IsInput = false
            };
        }

        static ParameterStaticBinding BindQueueInput(CustomAttributeData attrData, ParameterInfo p)
        {
            QueueInputAttribute queueInputAttr = QueueInputAttribute.Build(attrData);
            string queueName = queueInputAttr.QueueName;
            if (queueName == null)
            {
                queueName = p.Name;
            }

            return new QueueParameterStaticBinding
            {
                QueueName = queueName,
                IsInput = true
            };
        }
    }
}