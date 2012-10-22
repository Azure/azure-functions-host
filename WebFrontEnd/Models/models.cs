using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web;
using System.Web.Mvc;
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

    public class RegisterFuncSubmitModel
    {
        public string AccountName { get; set; }
        public string ContainerName { get; set; }
        public Uri Writeback { get; set; }
    }

    public class OverviewModel
    {
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
        public FunctionIndexEntity[] Functions { get; set; }
    }
}