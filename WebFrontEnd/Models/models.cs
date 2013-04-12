using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using DaasEndpoints;
using Orchestrator;
using RunnerInterfaces;

namespace WebFrontEnd.Controllers
{
    public class LogOnViewModel
    {
        [Required]
        [MinLength(3)]
        [MaxLength(15)] // sanity check
        [RegularExpression("[a-zA-Z0-9]+")] // especially useful since we use UserName in filename lookup
        public string UserName { get; set; }

        [Required]
        [DataType(DataType.Password)]
        [MaxLength(15)] // sanity check
        public string Password { get; set; }
    }

    public class FuncSubmitModel
    {
        public Uri Writeback { get; set; }
    }

    public class RegisterFuncSubmitModel : FuncSubmitModel
    {
        public string AccountName { get; set; }

        public string ContainerName { get; set; }
    }

    public class DeleteFuncSubmitModel : FuncSubmitModel
    {
        public string FunctionToDelete { get; set; }
    }

    public class OverviewModel
    {
        public string AccountName { get; set; }
        public string ExecutionSubstrate { get; set; }

        public int? QueueDepth { get; set; }

        public ServiceHealthStatus HealthStatus { get; set; }
    }

    public class RequestScanSubmitModel
    {
        public int CountScanned { get; set; }
    }

    public class ExecutionLogModel
    {
        public Uri[] Logs { get; set; }
    }

    public class FunctionSubmitModel
    {
        public string Result { get; set; }
    }

    public class FunctionListModel
    {
        public IEnumerable<IGrouping<string, FunctionDefinition>> Functions { get; set; }
    }

    public class BinderListModel
    {
        public Entry[] Binders { get; set; }

        public class Entry
        {
            public string TypeName { get; set; } // type this binder applies to

            public string AccountName { get; set; }

            public CloudBlobPath Path { get; set; }

            public string EntryPoint { get; set; }
        }
    }

    public class FunctionChainModel
    {
        public ICausalityReader Walker { get; set; }

        public IFunctionInstanceLookup Lookup { get; set; }

        public IEnumerable<ListNode> Nodes { get; set; }
    }

    // Convert tree into flat list so that it's easier to render
    public class ListNode
    {
        public FunctionInvokeRequest Func { get; set; }

        public int Depth { get; set; }
    }
}