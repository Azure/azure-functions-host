using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.WindowsAzure.Jobs.Dashboard.Models.Protocol;

namespace Microsoft.WindowsAzure.Jobs.Dashboard.Controllers
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

        public ServiceHealthStatusModel HealthStatus { get; set; }
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
        public IEnumerable<IGrouping<object, RunningFunctionDefinitionModel>> Functions { get; set; }

        public bool HasWarning { get; set; }
    }

    public class BinderListModel
    {
        public Entry[] Binders { get; set; }

        public class Entry
        {
            public string TypeName { get; set; } // type this binder applies to

            public string AccountName { get; set; }

            public CloudBlobPathModel Path { get; set; }

            public string EntryPoint { get; set; }
        }
    }

    public class FunctionChainModel
    {
        internal ICausalityReader Walker { get; set; }

        internal IFunctionInstanceLookup Lookup { get; set; }

        public IEnumerable<ListNode> Nodes { get; set; }

        // For computing the whole span of the chain. 
        public TimeSpan? Duration { get; set; }
    }

    // Convert tree into flat list so that it's easier to render
    public class ListNode
    {
        public ExecutionInstanceLogEntityModel Func { get; set; }

        public int Depth { get; set; }
    }

    // Show static information about the function 
    public class FunctionInfoModel
    {
        public FunctionDefinitionModel Descriptor { get; set; }

        public ParamModel[] Parameters { get; set; }

        // List of {name}
        public string[] KeyNames { get; set; }

        public Guid ReplayGuid { get; set; }

        // Purely cosmetic (we recompute this on the server for security)
        public string UploadContainerName { get; set; }
    }
}
