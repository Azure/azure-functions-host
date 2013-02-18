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

        // $$$ don't think we use this anymore. 
        // True iff we know we have read-only access to the table. 
        // This is used for optimizations. 
        public bool IsReadOnly { get; set; }

        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
            return new TableParameterRuntimeBinding
            {
                Table = new CloudTableDescriptor
                {
                    AccountConnectionString = inputs.AccountConnectionString,
                    TableName = this.TableName
                }
            };
        }

        public override ParameterRuntimeBinding BindFromInvokeString(IRuntimeBindingInputs inputs, string invokeString)
        {
            return new TableParameterRuntimeBinding
            {
                Table = new CloudTableDescriptor
                {
                    AccountConnectionString = inputs.AccountConnectionString,
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
    }
}