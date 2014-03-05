using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web.Http;
using AzureTables;
using Dashboard.Controllers;
using Dashboard.ViewModels;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.Jobs.Host;
using InternalWebJobTypes = Microsoft.WindowsAzure.Jobs.WebJobTypes;
using WebJobTypes = Dashboard.ViewModels.WebJobTypes;

namespace Dashboard.ApiControllers
{
    public class FunctionsController : ApiController
    {
        private readonly IFunctionsInJobReader _functionsInJobReader;
        private readonly IFunctionInstanceLookup _functionInstanceLookup;
        private readonly IFunctionTableLookup _functionTableLookup;
        private readonly IProcessTerminationSignalReader _terminationSignalReader;
        private AzureTable<FunctionLocation, FunctionStatsEntity> _invokeStatsTable;
        private readonly IRunningHostTableReader _heartbeatTable;
        private readonly IFunctionInstanceQuery _functionInstanceQuery;
        private readonly ICausalityReader _causalityReader;

        internal FunctionsController(
            IFunctionsInJobReader functionsInJobReader, 
            IFunctionInstanceLookup functionInstanceLookup,
            IFunctionTableLookup functionTableLookup,
            IProcessTerminationSignalReader terminationSignalReader,
            ICausalityReader causalityReader, 
            IFunctionInstanceQuery functionInstanceQuery, IRunningHostTableReader heartbeatTable, AzureTable<FunctionLocation, FunctionStatsEntity> invokeStatsTable)
        {
            _functionsInJobReader = functionsInJobReader;
            _functionInstanceLookup = functionInstanceLookup;
            _functionTableLookup = functionTableLookup;
            _terminationSignalReader = terminationSignalReader;
            _causalityReader = causalityReader;
            _functionInstanceQuery = functionInstanceQuery;
            _heartbeatTable = heartbeatTable;
            _invokeStatsTable = invokeStatsTable;
        }

        [Route("api/jobs/triggered/{jobName}/runs/{runId}/functions")]
        public IHttpActionResult GetFunctionsInJob(string jobName, string runId, string olderThan = null, string newerThan = null, int limit = 20)
        {
            return GetFunctionsInJob(WebJobTypes.Triggered, jobName, runId, olderThan, newerThan, limit);
        }

        [Route("api/jobs/continuous/{jobName}/functions")]
        public IHttpActionResult GetFunctionsInJob(string jobName, string olderThan = null,string newerThan = null, int limit = 20)
        {
            return GetFunctionsInJob(WebJobTypes.Continuous, jobName, null, olderThan, newerThan, limit);
        }

        private  IHttpActionResult GetFunctionsInJob(WebJobTypes webJobType, string jobName, string runId, string olderThan, string newerThan, int limit)
        {
            if (limit <= 0)
            {
                return BadRequest("limit should be an positive, non-zero integer.");
            }

            var runIdentifier = new WebJobRunIdentifier(Environment.GetEnvironmentVariable(WebSitesKnownKeyNames.WebSiteNameKey), (InternalWebJobTypes)webJobType, jobName, runId);


            var functionsInJob = _functionsInJobReader.GetFunctionInvocationsForJobRun(runIdentifier, olderThan, newerThan, limit);

            var items = from functionInJobEntry in functionsInJob.AsParallel()
                let invocation = _functionInstanceLookup.Lookup(functionInJobEntry.InvocationId)
                select new {functionInJobEntry.RowKey, Invocation = new InvocationLogViewModel(invocation)};
            return Ok(items.ToArray());
        }

        [Route("api/functions/definitions/{functionName}")]
        public IHttpActionResult GetFunctionDefinition(string functionName)
        {
            var func = _functionTableLookup.Lookup(functionName);

            if (func == null)
            {
                return NotFound();
            }

            return Ok(new {functionName = functionName, functionId = func.Location.GetId()});
        }

        [Route("api/functions/definitions/{functionName}/invocations")]
        public IHttpActionResult GetInvocationsForFunction(string functionName)
        {
            var func = _functionTableLookup.Lookup(functionName);

            if (func == null)
            {
                return NotFound();
            }

            var filter = new FunctionInstanceQueryFilter
            {
                Location = func.Location
            };

            var invocations = _functionInstanceQuery.GetRecent(20, filter)
                .Select(e => new {invocation = new InvocationLogViewModel(e)})
                .ToArray();

            return Ok(invocations);
        }

        [Route("api/functions/invocations/recent")]
        public IHttpActionResult GetRecentInvocations()
        {
            var filter = new FunctionInstanceQueryFilter();
            var invocations = _functionInstanceQuery
                .GetRecent(10, filter)
                .Select(x => new InvocationLogViewModel(x))
                .ToArray();

            return Ok(invocations);
        }
        
        [Route("api/functions/invocationsByIds")]
        public IHttpActionResult PostInvocationsByIds(Guid[] ids)
        {
            var items = from invocationId in ids.AsParallel()
                        let invocation = _functionInstanceLookup.Lookup(invocationId)
                        select new InvocationLogViewModel(invocation);
            return Ok(items.ToArray());
        }

        [Route("api/functions/invocations/{functionInvocationId}/children")]
        public IHttpActionResult GetInvocationChildren(Guid functionInvocationId, string olderThan = null, string newerThan = null, int limit = 20)
        {
            if (limit <= 0)
            {
                return BadRequest("limit should be an positive, non-zero integer.");
            }

            var children = _causalityReader.GetChildren(functionInvocationId, newerThan, olderThan, limit);

            var items = from childEntity in children.AsParallel()
                        let invocation = _functionInstanceLookup.Lookup(childEntity.Data.Payload.ChildGuid)
                        select new { childEntity.RowKey, Invocation = new InvocationLogViewModel(invocation) };
            return Ok(items.ToArray());
        }

        [Route("api/functions/invocations/{functionInvocationId}")]
        public IHttpActionResult GetFunctionInvocation(Guid functionInvocationId)
        {
            var func = _functionInstanceLookup.Lookup(functionInvocationId);

            if (func == null)
            {
                return NotFound();
            }

            var model = new FunctionInstanceDetailsViewModel();
            model.Invocation = new InvocationLogViewModel(func);
            model.TriggerReason = new TriggerReasonViewModel(func.FunctionInstance.TriggerReason);
            model.Trigger = model.TriggerReason.ToString();
            model.IsAborted = model.Invocation.Status == ViewModels.FunctionInstanceStatus.Running && _terminationSignalReader.IsTerminationRequested(func.HostInstanceId);

            // Do some analysis to find inputs, outputs, 

            var functionModel = _functionTableLookup.Lookup(func.FunctionInstance.Location.GetId());

            if (functionModel != null)
            {
                var descriptor = new FunctionDefinitionViewModel(functionModel);

                // Parallel arrays of static descriptor and actual instance info 
                ParameterRuntimeBinding[] args = func.FunctionInstance.Args;

                model.Parameters = LogAnalysis.GetParamInfo(descriptor.UnderlyingObject);
                LogAnalysis.ApplyRuntimeInfo(func.FunctionInstance, args, model.Parameters);
                LogAnalysis.ApplySelfWatchInfo(func.FunctionInstance, model.Parameters);
            }

            // fetch ancestor
            var parentGuid = func.FunctionInstance.TriggerReason.ParentGuid;
            if (parentGuid != Guid.Empty)
            {
                model.Ancestor = new InvocationLogViewModel(_functionInstanceLookup.Lookup(parentGuid));
            }

            return Ok(model);
        }

        [Route("api/functions/definitions")]
        public IHttpActionResult GetFunctionDefinitions()
        {
            var model = new DashboardIndexViewModel();
            var hearbeats = _heartbeatTable.ReadAll();
            model.FunctionStatisticsViewModels = _functionTableLookup
                .ReadAll()
                .Select(f => new FunctionStatisticsViewModel
                {
                    FunctionFullName = f.ToString(),
                    FunctionName = f.Location.GetShortName(),
                    IsRunning = DashboardController.HasValidHeartbeat(f, hearbeats),
                    FailedCount = 0,
                    SuccessCount = 0,
                    LastStartTime = f.Timestamp
                }).ToArray();

            var functionNames = new HashSet<string>(model.FunctionStatisticsViewModels.Select(x => x.FunctionFullName));

            var all = _invokeStatsTable.Enumerate();
            foreach (var item in all)
            {
                string rowKey = item["RowKey"];
                var funcExists = functionNames.Contains(rowKey);

                if (!funcExists)
                {
                    // ignore functions in stats but not found
                    continue;
                }

                var statsModel = model
                    .FunctionStatisticsViewModels
                    .FirstOrDefault(x =>
                        x.FunctionFullName == rowKey
                    );

                if (statsModel != null)
                {
                    var stats = ObjectBinderHelpers.ConvertDictToObject<FunctionStatsEntity>(item);
                    statsModel.FailedCount = stats.CountErrors;
                    statsModel.SuccessCount = stats.CountCompleted;
                }
            }
            return Ok(model);
        }
    }
}
