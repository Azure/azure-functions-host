using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using AzureTables;
using Dashboard.Controllers;
using Dashboard.Data;
using Dashboard.Protocols;
using Dashboard.ViewModels;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Host;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage;
using InternalWebJobTypes = Microsoft.Azure.Jobs.WebJobTypes;
using WebJobTypes = Dashboard.ViewModels.WebJobTypes;

namespace Dashboard.ApiControllers
{
    public class FunctionsController : ApiController
    {
        private readonly CloudStorageAccount _account;
        private readonly IInvocationLogLoader _invocationLogLoader;
        private readonly IFunctionInstanceLookup _functionInstanceLookup;
        private readonly IFunctionTableLookup _functionTableLookup;
        private readonly IProcessTerminationSignalReader _terminationSignalReader;
        private readonly IProcessTerminationSignalWriter _terminationSignalWriter;
        private readonly AzureTable<FunctionLocation, FunctionStatsEntity> _invokeStatsTable;
        private readonly IRunningHostTableReader _heartbeatTable;

        internal FunctionsController(
            CloudStorageAccount account,
            IInvocationLogLoader invocationLogLoader,
            IFunctionInstanceLookup functionInstanceLookup,
            IFunctionTableLookup functionTableLookup,
            IProcessTerminationSignalReader terminationSignalReader,
            IProcessTerminationSignalWriter terminationSignalWriter,
            IRunningHostTableReader heartbeatTable, 
            AzureTable<FunctionLocation, FunctionStatsEntity> invokeStatsTable)
        {
            _invocationLogLoader = invocationLogLoader;
            _account = account;
            _functionInstanceLookup = functionInstanceLookup;
            _functionTableLookup = functionTableLookup;
            _terminationSignalReader = terminationSignalReader;
            _terminationSignalWriter = terminationSignalWriter;
            _heartbeatTable = heartbeatTable;
            _invokeStatsTable = invokeStatsTable;
        }

        [Route("api/jobs/triggered/{jobName}/runs/{runId}/functions")]
        public IHttpActionResult GetFunctionsInJob(string jobName, string runId, [FromUri]PagingInfo pagingInfo)
        {
            return GetFunctionsInJob(WebJobTypes.Triggered, jobName, runId, pagingInfo);
        }

        [Route("api/jobs/continuous/{jobName}/functions")]
        public IHttpActionResult GetFunctionsInJob(string jobName, [FromUri]PagingInfo pagingInfo)
        {
            return GetFunctionsInJob(WebJobTypes.Continuous, jobName, null, pagingInfo);
        }

        private IHttpActionResult GetFunctionsInJob(WebJobTypes webJobType, string jobName, string runId, [FromUri] PagingInfo pagingInfo)
        {
            if (pagingInfo.Limit <= 0)
            {
                return BadRequest("limit should be an positive, non-zero integer.");
            }

            var runIdentifier = new WebJobRunIdentifier(Environment.GetEnvironmentVariable(WebSitesKnownKeyNames.WebSiteNameKey), (InternalWebJobTypes)webJobType, jobName, runId);

            var invocations = _invocationLogLoader.GetInvocationsInJob(runIdentifier.GetKey(), pagingInfo);
            return Ok(invocations);
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
        public IHttpActionResult GetInvocationsForFunction(string functionName, [FromUri]PagingInfo pagingInfo)
        {
            var func = _functionTableLookup.Lookup(functionName);

            if (func == null)
            {
                return NotFound();
            }

            var invocations = _invocationLogLoader.GetInvocationsInFunction(func.Location.GetId(), pagingInfo);
            return Ok(invocations);
        }

        [Route("api/functions/invocations/recent")]
        public IHttpActionResult GetRecentInvocations([FromUri]PagingInfo pagingInfo)
        {
            var invocations = _invocationLogLoader.GetRecentInvocations(pagingInfo);
            return Ok(invocations);
        }
        
        [Route("api/functions/invocationsByIds")]
        public IHttpActionResult PostInvocationsByIds(Guid[] ids)
        {
            var items = _invocationLogLoader.GetInvocationsByIds(ids);
            return Ok(items.ToArray());
        }

        [Route("api/functions/invocations/{functionInvocationId}/children")]
        public IHttpActionResult GetInvocationChildren(Guid functionInvocationId, [FromUri]PagingInfo pagingInfo)
        {
            var invocations = _invocationLogLoader.GetInvocationChildren(functionInvocationId, pagingInfo);
            return Ok(invocations);
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
            model.Invocation = new InvocationLogViewModel(func, _invocationLogLoader.GetHeartbeat(func.HostInstanceId));
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
                ExecutionInstanceLogEntity ancestor = _functionInstanceLookup.Lookup(parentGuid);
                DateTime? heartbeat;

                if (ancestor != null)
                {
                    heartbeat = _invocationLogLoader.GetHeartbeat(ancestor.HostInstanceId);
                }
                else
                {
                    heartbeat = null;
                }

                model.Ancestor = new InvocationLogViewModel(ancestor, heartbeat);
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
                    IsRunning = FunctionController.HasValidHeartbeat(f, hearbeats),
                    IsOldHost = !f.HostVersion.HasValue,
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

            model.StorageAccountName = _account.Credentials.AccountName;

            return Ok(model);
        }

        [Route("api/hostInstances/{hostInstanceId}/abort")]
        public IHttpActionResult Abort(Guid hostInstanceId)
        {
            _terminationSignalWriter.RequestTermination(hostInstanceId);

            return Ok();
        }
    }
}
