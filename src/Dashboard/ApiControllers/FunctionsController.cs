using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Caching;
using System.Web.Http;
using Dashboard.Data;
using Dashboard.HostMessaging;
using Dashboard.ViewModels;
using Microsoft.Azure.Jobs.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using InternalWebJobTypes = Microsoft.Azure.Jobs.Protocols.WebJobTypes;
using WebJobTypes = Dashboard.ViewModels.WebJobTypes;

namespace Dashboard.ApiControllers
{
    public class FunctionsController : ApiController
    {
        private readonly CloudStorageAccount _account;
        private readonly CloudTableClient _tableClient;
        private readonly IFunctionInstanceLookup _functionInstanceLookup;
        private readonly IFunctionLookup _functionLookup;
        private readonly IFunctionIndexReader _functionIndexReader;
        private readonly IHeartbeatValidityMonitor _heartbeatMonitor;
        private readonly IAborter _aborter;
        private readonly IRecentInvocationIndexReader _recentInvocationsReader;
        private readonly IRecentInvocationIndexByFunctionReader _recentInvocationsByFunctionReader;
        private readonly IRecentInvocationIndexByJobRunReader _recentInvocationsByJobRunReader;
        private readonly IRecentInvocationIndexByParentReader _recentInvocationsByParentReader;
        private readonly IFunctionStatisticsReader _statisticsReader;
        private readonly ConcurrentDictionary<string, bool?> _cachedHostHeartbeats =
            new ConcurrentDictionary<string, bool?>();
        private readonly ConcurrentDictionary<string, Tuple<int, int>> _cachedStatistics =
            new ConcurrentDictionary<string, Tuple<int, int>>();

        internal FunctionsController(
            CloudStorageAccount account,
            CloudTableClient tableClient,
            IFunctionInstanceLookup functionInstanceLookup,
            IFunctionLookup functionLookup,
            IFunctionIndexReader functionIndexReader,
            IHeartbeatValidityMonitor heartbeatMonitor,
            IAborter aborter,
            IRecentInvocationIndexReader recentInvocationsReader,
            IRecentInvocationIndexByFunctionReader recentInvocationsByFunctionReader,
            IRecentInvocationIndexByJobRunReader recentInvocationsByJobRunReader,
            IRecentInvocationIndexByParentReader recentInvocationsByParentReader,
            IFunctionStatisticsReader statisticsReader)
        {
            _account = account;
            _tableClient = tableClient;
            _functionInstanceLookup = functionInstanceLookup;
            _functionLookup = functionLookup;
            _functionIndexReader = functionIndexReader;
            _heartbeatMonitor = heartbeatMonitor;
            _aborter = aborter;
            _recentInvocationsReader = recentInvocationsReader;
            _recentInvocationsByFunctionReader = recentInvocationsByFunctionReader;
            _recentInvocationsByJobRunReader = recentInvocationsByJobRunReader;
            _recentInvocationsByParentReader = recentInvocationsByParentReader;
            _statisticsReader = statisticsReader;
        }

        [Route("api/jobs/triggered/{jobName}/runs/{runId}/functions")]
        public IHttpActionResult GetFunctionsInJob(string jobName, string runId, [FromUri]ContainerPagingInfo pagingInfo)
        {
            return GetFunctionsInJob(WebJobTypes.Triggered, jobName, runId, pagingInfo);
        }

        [Route("api/jobs/continuous/{jobName}/functions")]
        public IHttpActionResult GetFunctionsInJob(string jobName, [FromUri]ContainerPagingInfo pagingInfo)
        {
            return GetFunctionsInJob(WebJobTypes.Continuous, jobName, null, pagingInfo);
        }

        /// <summary>
        /// This method determines if a warning should be shown in the dashboard that only older hosts exist.
        /// </summary>
        /// <param name="alreadyFoundNoNewerEntries">
        /// true if it is known that there are no records from a new host; false if unknown
        /// </param>
        /// <returns>True if warning should be shown; false otherwise</returns>
        private bool OnlyAlpha2HostExists(bool alreadyFoundNoNewerEntries)
        {
            const string Alpha2TableName = "AzureJobsFunctionIndex5";

            try
            {
                return _tableClient.GetTableReference(Alpha2TableName).Exists() &&
                    (alreadyFoundNoNewerEntries || !FunctionIndexHasEntry());
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool FunctionIndexHasEntry()
        {
            var results = _functionIndexReader.Read(1, null);

            if (results == null)
            {
                return false;
            }

            var resultsList = results.Results;

            if (resultsList == null)
            {
                return false;
            }

            return resultsList.Any();
        }

        private IHttpActionResult GetFunctionsInJob(WebJobTypes webJobType, string jobName, string runId, [FromUri] ContainerPagingInfo pagingInfo)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (pagingInfo == null)
            {
                return BadRequest();
            }

            var runIdentifier = new WebJobRunIdentifier(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"),
                (InternalWebJobTypes)webJobType, jobName, runId);

            IResultSegment<RecentInvocationEntry> indexSegment = _recentInvocationsByJobRunReader.Read(runIdentifier,
                pagingInfo.Limit, pagingInfo.ContinuationToken);
            InvocationLogSegment results;

            if (indexSegment != null)
            {
                results = new InvocationLogSegment
                {
                    Entries = CreateInvocationEntries(indexSegment.Results),
                    ContinuationToken = indexSegment.ContinuationToken
                };
            }
            else
            {
                results = new InvocationLogSegment
                {
                    IsOldHost = OnlyAlpha2HostExists(alreadyFoundNoNewerEntries: false)
                };
            }

            return Ok(results);
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
        public IHttpActionResult GetInvocationsForFunction(string functionId, [FromUri]ContainerPagingInfo pagingInfo)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (pagingInfo == null)
            {
                return BadRequest();
            }

            var func = _functionLookup.Read(functionId);

            if (func == null)
            {
                return NotFound();
            }

            IResultSegment<RecentInvocationEntry> indexSegment = _recentInvocationsByFunctionReader.Read(func.Id,
                pagingInfo.Limit, pagingInfo.ContinuationToken);
            return Invocations(indexSegment);
        }

        [Route("api/functions/invocations/recent")]
        public IHttpActionResult GetRecentInvocations([FromUri]ContainerPagingInfo pagingInfo)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (pagingInfo == null)
            {
                return BadRequest();
            }

            IResultSegment<RecentInvocationEntry> indexSegment =
                _recentInvocationsReader.Read(pagingInfo.Limit, pagingInfo.ContinuationToken);
            return Invocations(indexSegment);
        }

        private IHttpActionResult Invocations(IResultSegment<RecentInvocationEntry> indexSegment)
        {
            if (indexSegment == null)
            {
                return Ok();
            }

            InvocationLogSegment results = new InvocationLogSegment
            {
                Entries = CreateInvocationEntries(indexSegment.Results),
                ContinuationToken = indexSegment.ContinuationToken
            };

            return Ok(results);
        }

        private IEnumerable<InvocationLogViewModel> CreateInvocationEntries(IEnumerable<RecentInvocationEntry> indexEntries)
        {
            return CreateInvocationEntries(indexEntries.Select(e => e.Id));
        }

        private IEnumerable<InvocationLogViewModel> CreateInvocationEntries(IEnumerable<Guid> ids)
        {
            IEnumerable<InvocationLogViewModel> enumerable = from Guid id in ids
                                                             let model = CreateInvocationEntry(id)
                                                             where model != null
                                                             select model;

            return enumerable.ToList();
        }

        private InvocationLogViewModel CreateInvocationEntry(Guid id)
        {
            InvocationLogViewModel invocationModel = null;
            string cacheKey = "INVOCATION_MODEL_" + id;
            if (HttpRuntime.Cache != null)
            {
                invocationModel = HttpRuntime.Cache.Get(cacheKey) as InvocationLogViewModel;
                if (invocationModel == null)
                {
                    var invocation = _functionInstanceLookup.Lookup(id);
                    if (invocation != null)
                    {
                        invocationModel = new InvocationLogViewModel(invocation, HostInstanceHasHeartbeat(invocation));
                        if (invocationModel.IsFinal())
                        {
                            HttpRuntime.Cache.Insert(cacheKey, invocationModel, null, Cache.NoAbsoluteExpiration, TimeSpan.FromMinutes(30));
                        }
                    }
                }
            }
            else
            {
                var invocation = _functionInstanceLookup.Lookup(id);
                if (invocation != null)
                {
                    invocationModel = new InvocationLogViewModel(invocation, HostInstanceHasHeartbeat(invocation));
                }
            }

            return invocationModel;
        }

        [Route("api/functions/invocationsByIds")]
        public IHttpActionResult PostInvocationsByIds(Guid[] ids)
        {
            return Ok(CreateInvocationEntries(ids));
        }

        [Route("api/functions/invocations/{functionInvocationId}/children")]
        public IHttpActionResult GetInvocationChildren(Guid functionInvocationId, [FromUri]ContainerPagingInfo pagingInfo)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (pagingInfo == null)
            {
                return BadRequest();
            }

            IResultSegment<RecentInvocationEntry> indexSegment = _recentInvocationsByParentReader.Read(
                functionInvocationId, pagingInfo.Limit, pagingInfo.ContinuationToken);
            return Invocations(indexSegment);
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
        public IHttpActionResult GetFunctionDefinitions([FromUri]ContainerPagingInfo pagingInfo)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            IResultSegment<FunctionSnapshot> indexSegment = _functionIndexReader.Read(pagingInfo.Limit,
                pagingInfo.ContinuationToken);

            var model = new FunctionStatisticsSegment();
            bool foundItem;

            if (indexSegment != null && indexSegment.Results != null)
            {
                IEnumerable<FunctionStatisticsViewModel> query = indexSegment.Results
                    .Select(function => new FunctionStatisticsViewModel
                    {
                        FunctionId = function.Id,
                        FunctionFullName = function.FullName,
                        FunctionName = function.ShortName,
                        IsRunning = HostHasHeartbeat(function).GetValueOrDefault(true),
                        FailedCount = 0,
                        SuccessCount = 0,
                        WhenUtc = function.HostVersion.UtcDateTime
                    });

                model.Entries = query.ToArray();
                foundItem = query.Any();
                model.ContinuationToken = indexSegment.ContinuationToken;
            }
            else
            {
                foundItem = false;
            }

            model.StorageAccountName = _account.Credentials.AccountName;

            if (foundItem)
            {
                model.IsOldHost = OnlyAlpha2HostExists(alreadyFoundNoNewerEntries: true);
            }

            foreach (FunctionStatisticsViewModel statisticsModel in model.Entries)
            {
                if (statisticsModel == null)
                {
                    continue;
                }

                Tuple<int, int> counts = GetStatistics(statisticsModel.FunctionId);
                statisticsModel.SuccessCount = counts.Item1;
                statisticsModel.FailedCount = counts.Item2;
            }

            return Ok(model);
        }

        private Tuple<int, int> GetStatistics(string functionId)
        {
            if (_cachedStatistics.ContainsKey(functionId))
            {
                return _cachedStatistics[functionId];
            }

            FunctionStatistics entity = _statisticsReader.Lookup(functionId);
            int succeededCount;
            int failedCount;

            if (entity == null)
            {
                succeededCount = 0;
                failedCount = 0;
            }
            else
            {
                succeededCount = entity.SucceededCount;
                failedCount = entity.FailedCount;
            }

            Tuple<int, int> results = new Tuple<int, int>(succeededCount, failedCount);
            _cachedStatistics.AddOrUpdate(functionId, (_) => results, (ignore1, ignore2) => results);
            return results;
        }

        [Route("api/hostInstances/{instanceQueueName}/abort")]
        public IHttpActionResult Abort(string instanceQueueName)
        {
            _aborter.RequestHostInstanceAbort(instanceQueueName);

            return Ok();
        }

        private bool? HostHasHeartbeat(FunctionSnapshot function)
        {
            string hostId = function.HeartbeatSharedContainerName + "/" + function.HeartbeatSharedDirectoryName;

            if (_cachedHostHeartbeats.ContainsKey(hostId))
            {
                return _cachedHostHeartbeats[hostId];
            }
            else
            {
                bool? heartbeat = HostHasHeartbeat(_heartbeatMonitor, function);
                _cachedHostHeartbeats.AddOrUpdate(hostId, heartbeat, (ignore1, ignore2) => heartbeat);
                return heartbeat;
            }
        }

        internal static bool? HostHasHeartbeat(IHeartbeatValidityMonitor heartbeatMonitor, FunctionSnapshot snapshot)
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

        private bool? HostInstanceHasHeartbeat(FunctionInstanceSnapshot snapshot)
        {
            HeartbeatDescriptor heartbeat = snapshot.Heartbeat;

            if (heartbeat == null)
            {
                return null;
            }

            return _heartbeatMonitor.IsInstanceHeartbeatValid(heartbeat.SharedContainerName,
                heartbeat.SharedDirectoryName, heartbeat.InstanceBlobName, heartbeat.ExpirationInSeconds);
        }
    }
}
