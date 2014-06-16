using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using AzureTables;
using Dashboard.Data;
using Dashboard.HostMessaging;
using Dashboard.InvocationLog;
using Dashboard.ViewModels;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Protocols;
using Microsoft.WindowsAzure.Storage;
using InternalWebJobTypes = Microsoft.Azure.Jobs.Protocols.WebJobTypes;
using WebJobTypes = Dashboard.ViewModels.WebJobTypes;

namespace Dashboard.ApiControllers
{
    public class FunctionsController : ApiController
    {
        private readonly CloudStorageAccount _account;
        private readonly IInvocationLogLoader _invocationLogLoader;
        private readonly IFunctionInstanceLookup _functionInstanceLookup;
        private readonly IFunctionLookup _functionLookup;
        private readonly IHeartbeatMonitor _heartbeatMonitor;
        private readonly IAborter _aborter;
        private readonly AzureTable<string, FunctionStatsEntity> _invokeStatsTable;
        private readonly ConcurrentDictionary<string, bool?> _cachedHostHeartbeats =
            new ConcurrentDictionary<string, bool?>();

        internal FunctionsController(
            CloudStorageAccount account,
            IInvocationLogLoader invocationLogLoader,
            IFunctionInstanceLookup functionInstanceLookup,
            IFunctionLookup functionLookup,
            IHeartbeatMonitor heartbeatMonitor,
            IAborter aborter,
            AzureTable<string, FunctionStatsEntity> invokeStatsTable)
        {
            _account = account;
            _invocationLogLoader = invocationLogLoader;
            _functionInstanceLookup = functionInstanceLookup;
            _functionLookup = functionLookup;
            _heartbeatMonitor = heartbeatMonitor;
            _aborter = aborter;
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

        /// <summary>
        /// This method determines if a warning should be shown in the dashboard by
        /// checking if there are old data model tables and, if so, check if there are already
        /// new records (in which case the warning would not be shown).
        /// </summary>
        /// <param name="noNewRecords">true if it is known that there are new records; false if unknown</param>
        /// <returns>True if warning should be shown; false otherwise</returns>
        private bool OldHostExists(bool noNewRecords)
        {
            try
            {
                return _account.CreateCloudTableClient()
                    .GetTableReference(DashboardTableNames.OldFunctionInJobsIndex).Exists() &&
                    (noNewRecords || _functionLookup.ReadAll().Count == 0);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private IHttpActionResult GetFunctionsInJob(WebJobTypes webJobType, string jobName, string runId, [FromUri] PagingInfo pagingInfo)
        {
            if (pagingInfo.Limit <= 0)
            {
                return BadRequest("limit should be an positive, non-zero integer.");
            }

            var runIdentifier = new Microsoft.Azure.Jobs.Protocols.WebJobRunIdentifier(Environment.GetEnvironmentVariable(Microsoft.Azure.Jobs.Host.WebSitesKnownKeyNames.WebSiteNameKey), (InternalWebJobTypes)webJobType, jobName, runId);

            var invocations = _invocationLogLoader.GetInvocationsInJob(runIdentifier.GetKey(), pagingInfo);

            if (invocations.Entries.Length == 0)
            {
                invocations.IsOldHost = OldHostExists(false);
            }

            return Ok(invocations);
        }

        [Route("api/functions/definitions/{functionId}")]
        public IHttpActionResult GetFunctionDefinition(string functionId)
        {
            var func = _functionLookup.Read(functionId);

            if (func == null)
            {
                return NotFound();
            }

            return Ok(new { functionName = func.FullName, functionId = func.Id });
        }

        [Route("api/functions/definitions/{functionId}/invocations")]
        public IHttpActionResult GetInvocationsForFunction(string functionId, [FromUri]PagingInfo pagingInfo)
        {
            var func = _functionLookup.Read(functionId);

            if (func == null)
            {
                return NotFound();
            }

            var invocations = _invocationLogLoader.GetInvocationsInFunction(func.Id, pagingInfo);
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
            model.Invocation = new InvocationLogViewModel(func, HostHasHeartbeat(func));
            model.TriggerReason = new TriggerReasonViewModel(func);
            model.Trigger = model.TriggerReason.ToString();
            model.IsAborted = model.Invocation.Status == ViewModels.FunctionInstanceStatus.Running
                && _aborter.HasRequestedHostInstanceAbort(func.InstanceQueueName);
            model.Parameters = CreateParameterModels(func);

            // fetch ancestor
            var parentGuid = func.ParentId;
            if (parentGuid.HasValue)
            {
                FunctionInstanceSnapshot ancestor = _functionInstanceLookup.Lookup(parentGuid.Value);
                bool? hasValidHeartbeat;

                if (ancestor != null)
                {
                    hasValidHeartbeat = HostHasHeartbeat(ancestor);
                }
                else
                {
                    hasValidHeartbeat = null;
                }

                model.Ancestor = new InvocationLogViewModel(ancestor, hasValidHeartbeat);
            }

            return Ok(model);
        }

        private static ParamModel[] CreateParameterModels(FunctionInstanceSnapshot snapshot)
        {
            List<ParamModel> models = new List<ParamModel>();

            IDictionary<string, FunctionInstanceArgument> parameters = snapshot.Arguments;
            IDictionary<string, string> selfWatch = LogAnalysis.GetParameterSelfWatch(snapshot);

            foreach (KeyValuePair<string, FunctionInstanceArgument> parameter in parameters)
            {
                string name = parameter.Key;
                FunctionInstanceArgument argument = parameter.Value;

                ParamModel model = new ParamModel
                {
                    Name = name,
                    ArgInvokeString = argument.Value
                };

                if (selfWatch.ContainsKey(name))
                {
                    model.SelfWatch = selfWatch[name];
                }

                if (argument.IsBlob)
                {
                    model.ExtendedBlobModel = LogAnalysis.CreateExtendedBlobModel(snapshot, argument);
                }

                models.Add(model);
            }

            return models.ToArray();
        }

        [Route("api/functions/definitions")]
        public IHttpActionResult GetFunctionDefinitions([FromUri]PagingInfo pagingInfo)
        {
            var model = new FunctionStatisticsPageViewModel();
            IEnumerable<FunctionStatisticsViewModel> query = _functionLookup
                .ReadAll()
                .Select(f => new FunctionStatisticsViewModel
                {
                    FunctionId = f.Id,
                    FunctionFullName = f.FullName,
                    FunctionName = f.ShortName,
                    IsRunning = HostHasHeartbeat(f).GetValueOrDefault(true),
                    FailedCount = 0,
                    SuccessCount = 0
                })
                .OrderBy(f => f.FunctionId);

            var lastElement = query.LastOrDefault();
            if (lastElement != null)
            {
                if (!String.IsNullOrEmpty(pagingInfo.NewerThan))
                {
                    query = query.Where(f => f.FunctionId.CompareTo(pagingInfo.NewerThan) < 0);
                }

                if (!String.IsNullOrEmpty(pagingInfo.OlderThan))
                {
                    query = query.Where(f => f.FunctionId.CompareTo(pagingInfo.OlderThan) > 0);
                }

                if (!String.IsNullOrEmpty(pagingInfo.OlderThanOrEqual))
                {
                    query = query.Where(f => f.FunctionId.CompareTo(pagingInfo.OlderThanOrEqual) >= 0);
                }

                if (pagingInfo.Limit != null)
                {
                    query = query.Take((int)pagingInfo.Limit);
                }
            }

            model.Entries = query.ToArray();
            model.HasMore = lastElement != null ?
                !model.Entries.Any(f => f.FunctionId.Equals(lastElement.FunctionId)) :
                false;
            model.StorageAccountName = _account.Credentials.AccountName;

            if (lastElement == null)
            {
                model.IsOldHost = OldHostExists(true);
            }

            var all = _invokeStatsTable.Enumerate();
            foreach (var item in all)
            {
                var statsModel = model.Entries.FirstOrDefault(x =>
                    x.FunctionId.Equals(item["RowKey"]));

                if (statsModel != null)
                {
                    var stats = ObjectBinderHelpers.ConvertDictToObject<FunctionStatsEntity>(item);
                    statsModel.FailedCount = stats.CountErrors;
                    statsModel.SuccessCount = stats.CountCompleted;
                }
            }

            return Ok(model);
        }

        [Route("api/hostInstances/{instanceQueueName}/abort")]
        public IHttpActionResult Abort(string instanceQueueName)
        {
            _aborter.RequestHostInstanceAbort(instanceQueueName);

            return Ok();
        }

        private bool? HostHasHeartbeat(FunctionSnapshot snapshot)
        {
            string hostId = snapshot.HeartbeatSharedContainerName + "/" + snapshot.HeartbeatSharedDirectoryName;

            if (_cachedHostHeartbeats.ContainsKey(hostId))
            {
                return _cachedHostHeartbeats[hostId];
            }
            else
            {
                bool? heartbeat = HostHasHeartbeat(_heartbeatMonitor, snapshot);
                _cachedHostHeartbeats.AddOrUpdate(hostId, heartbeat, (ignore1, ignore2) => heartbeat);
                return heartbeat;
            }
        }

        internal static bool? HostHasHeartbeat(IHeartbeatMonitor heartbeatMonitor, FunctionSnapshot snapshot)
        {
            int? expiration = snapshot.HeartbeatExpirationInSeconds;

            if (!expiration.HasValue)
            {
                return null;
            }

            return heartbeatMonitor.IsSharedHeartbeatValid(snapshot.HeartbeatSharedContainerName,
                snapshot.HeartbeatSharedDirectoryName, expiration.Value);
        }

        private bool? HostHasHeartbeat(FunctionInstanceSnapshot snapshot)
        {
            HeartbeatDescriptor heartbeat = snapshot.Heartbeat;

            if (heartbeat == null)
            {
                return null;
            }

            return _heartbeatMonitor.IsSharedHeartbeatValid(heartbeat.SharedContainerName,
                heartbeat.SharedDirectoryName, heartbeat.ExpirationInSeconds);
        }
    }
}
