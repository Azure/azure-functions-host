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
using RunnerHost;
using RunnerInterfaces;

namespace Orchestrator
{
    public class TableParameterStaticBinding : ParameterStaticBinding
    {
        public string TableName { get; set; }

        // True iff we know we have read-only access to the table. 
        // This is used for optimizations. 
        public bool IsReadOnly { get; set; }

        public override ParameterRuntimeBinding Bind(RuntimeBindingInputs inputs)
        {
            return new TableParameterRuntimeBinding
            {
                Table = new CloudTableDescriptor
                {
                    AccountConnectionString = Utility.GetConnectionString(inputs._account),
                    TableName = this.TableName
                }
            };
        }

        public override ParameterRuntimeBinding BindFromInvokeString(CloudStorageAccount account, string invokeString)
        {
            return new TableParameterRuntimeBinding
            {
                Table = new CloudTableDescriptor
                {
                    AccountConnectionString = Utility.GetConnectionString(account),
                    TableName = invokeString
                }
            };
        }

        public override string Description
        {
            get {
                return string.Format("Access table: {0}", this.TableName);
            }
        }    

        public override TriggerType GetTriggerType()
        {
            if (this.IsReadOnly)
            {
                // A true read-only table is side-effect free. It's like a giant literal. 
                return TriggerType.Ignore;
            }
            else
            {
                // Mutable tables have unknown side-effects.
                return TriggerType.Unknown; 
            }
        }
    }
}