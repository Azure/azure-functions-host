using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using DaasEndpoints;
using Executor;
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
        public string VersionInformation { get; set; }

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
        public IEnumerable<IGrouping<object, FunctionDefinition>> Functions { get; set; }
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

        // For computing the whole span of the chain. 
        public TimeSpan? Duration { get; set; }
    }

    // Convert tree into flat list so that it's easier to render
    public class ListNode
    {
        public ExecutionInstanceLogEntity Func { get; set; }

        public int Depth { get; set; }
    }

    // Show static information about the function 
    public class FunctionInfoModel
    {
        public FunctionDefinition Descriptor { get; set; }

        public ParamModel[] Parameters { get; set; }

        // List of {name}
        public string[] KeyNames { get; set; }

        public Guid ReplayGuid { get; set; }

        // Purely cosmetic (we recompute this on the server for security)
        public string UploadContainerName { get; set; }
    }
}