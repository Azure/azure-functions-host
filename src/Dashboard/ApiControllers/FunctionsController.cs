// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Caching;
using System.Web.Http;
using Dashboard.Data;
using Dashboard.HostMessaging;
using Dashboard.ViewModels;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

using InternalWebJobTypes = Microsoft.Azure.WebJobs.Protocols.WebJobTypes;
using WebJobTypes = Dashboard.ViewModels.WebJobType;

namespace Dashboard.ApiControllers
{
    public class FunctionsController : ApiController
    {
        private readonly CloudStorageAccount _account;
        private readonly CloudBlobClient _blobClient;
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
        private readonly ILogReader _reader;

        internal FunctionsController(
            CloudStorageAccount account,
            CloudBlobClient blobClient,
            IFunctionInstanceLookup functionInstanceLookup,
            IFunctionLookup functionLookup,
            IFunctionIndexReader functionIndexReader,
            IHeartbeatValidityMonitor heartbeatMonitor,
            IAborter aborter,
            IRecentInvocationIndexReader recentInvocationsReader,
            IRecentInvocationIndexByFunctionReader recentInvocationsByFunctionReader,
            IRecentInvocationIndexByJobRunReader recentInvocationsByJobRunReader,
            IRecentInvocationIndexByParentReader recentInvocationsByParentReader,
            IFunctionStatisticsReader statisticsReader,
            ILogReader reader)
        {
            _account = account;
            _blobClient = blobClient;
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
            _reader = reader;
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
        /// This method determines if a warning should be shown in the dashboard that only older hosts exist.
        /// </summary>
        /// <param name="alreadyFoundNoNewerEntries">
        /// true if it is known that there are no records from a new host; false if unknown
        /// </param>
        /// <returns>True if warning should be shown; false otherwise</returns>
        private bool OnlyBeta1HostExists(bool alreadyFoundNoNewerEntries)
        {
            const string Beta1ContainerName = "azure-jobs-dashboard-hosts";

            try
            {
                return _blobClient.GetContainerReference(Beta1ContainerName).Exists() &&
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

        private IHttpActionResult GetFunctionsInJob(WebJobTypes webJobType, string jobName, string runId, [FromUri] PagingInfo pagingInfo)
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
                    IsOldHost = OnlyBeta1HostExists(alreadyFoundNoNewerEntries: false)
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
        public IHttpActionResult GetInvocationsForFunction(string functionId, [FromUri]PagingInfo pagingInfo)
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

        // Returns a sparse array of (StartBucket, Start, TotalPass, TotalFail, TotalRun)                
        [Route("api/functions/invocations/{functionId}/timeline")]
        public async Task<IHttpActionResult> GetRecentInvocationsTimeline(
            string functionId, 
            [FromUri]PagingInfo pagingInfo,
            DateTime? start = null,
            DateTime? end = null)
        {
            if (end == null)
            {
                end = DateTime.UtcNow;
            }
            if (start == null)
            {
                start = end.Value.AddDays(-7);
            }
            if (pagingInfo == null)
            {
                return BadRequest();
            }

            var segment = await _reader.GetAggregateStatsAsync(functionId, start.Value, end.Value, null);
            var entities = segment.Results;

            var result = Array.ConvertAll(entities, entity => new
            {
                 StartBucket = entity.TimeBucket,
                 Start = entity.Time,
                 TotalPass = entity.TotalPass,
                 TotalFail = entity.TotalFail,
                 TotalRun = entity.TotalRun
            });

            return Ok(result);
        }

        // Returns sparse array of for when the containers are run.
        [Route("api/containers/timeline")]
        public async Task<IHttpActionResult> GetContainerTimeline(
            [FromUri]PagingInfo pagingInfo,
            DateTime? start = null,
            DateTime? end = null)
        {
            if (end == null)
            {
                end = DateTime.UtcNow;
            }
            if (start == null)
            {
                start = end.Value.AddDays(-7);
            }
            if (pagingInfo == null)
            {
                return BadRequest();
            }

            var segment = await _reader.GetActiveContainerTimelineAsync(start.Value, end.Value, null);
            return Ok(segment);
        }

        [Route("api/functions/invocations/recent")]
        public IHttpActionResult GetRecentInvocations([FromUri]PagingInfo pagingInfo)
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
                return Ok(new InvocationLogSegment { Entries = new InvocationLogViewModel[0] });
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
            return indexEntries.Select(e => CreateInvocationEntry(e)).ToList();
        }

        private IEnumerable<InvocationLogViewModel> CreateInvocationEntries(IEnumerable<Guid> ids)
        {
            IEnumerable<InvocationLogViewModel> enumerable = from Guid id in ids
                                                             let model = CreateInvocationEntry(id)
                                                             where model != null
                                                             select model;

            return enumerable.ToList();
        }

        private InvocationLogViewModel CreateInvocationEntry(RecentInvocationEntry entry)
        {
            Debug.Assert(entry != null);

            var metadataSnapshot = new FunctionInstanceSnapshot();
            metadataSnapshot.Id = entry.Id;
            metadataSnapshot.DisplayTitle = entry.DisplayTitle;
            metadataSnapshot.StartTime = entry.StartTime;
            metadataSnapshot.EndTime = entry.EndTime;
            metadataSnapshot.Succeeded = entry.Succeeded;
            metadataSnapshot.Heartbeat = entry.Heartbeat;
            return new InvocationLogViewModel(metadataSnapshot, HostInstanceHasHeartbeat(metadataSnapshot));
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
        public IHttpActionResult GetInvocationChildren(Guid functionInvocationId, [FromUri]PagingInfo pagingInfo)
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
            model.Invocation = new InvocationLogViewModel(func, HostInstanceHasHeartbeat(func));
            model.TriggerReason = new TriggerReasonViewModel(func);
            model.Trigger = model.TriggerReason.ToString();
            model.IsAborted = model.Invocation.Status == ViewModels.FunctionInstanceStatus.Running
                && _aborter.HasRequestedHostInstanceAbort(func.InstanceQueueName);
            model.Parameters = CreateParameterModels(_account, func);

            // fetch ancestor
            var parentGuid = func.ParentId;
            if (parentGuid.HasValue)
            {
                FunctionInstanceSnapshot ancestor = _functionInstanceLookup.Lookup(parentGuid.Value);
                bool? hasValidHeartbeat;

                if (ancestor != null)
                {
                    hasValidHeartbeat = HostInstanceHasHeartbeat(ancestor);
                }
                else
                {
                    hasValidHeartbeat = null;
                }

                model.Ancestor = new InvocationLogViewModel(ancestor, hasValidHeartbeat);
            }

            return Ok(model);
        }

        private static ParameterModel[] CreateParameterModels(CloudStorageAccount account, FunctionInstanceSnapshot snapshot)
        {
            List<ParameterModel> models = new List<ParameterModel>();

            // It's important that we order any virtual parameters (e.g. Singleton parameters) to the end
            // so they don't interfere with actual function parameter ordering
            var parameters = snapshot.Arguments.OrderBy(p => p.Value.IsVirtualParameter);
            IDictionary<string, ParameterLog> parameterLogs = LogAnalysis.GetParameterLogs(account, snapshot);

            foreach (KeyValuePair<string, FunctionInstanceArgument> parameter in parameters)
            {
                string name = parameter.Key;
                FunctionInstanceArgument argument = parameter.Value;
                ParameterLog log;

                if (parameterLogs != null && parameterLogs.ContainsKey(name))
                {
                    log = parameterLogs[name];
                }
                else
                {
                    log = null;
                }

                LogAnalysis.AddParameterModels(name, argument, log, snapshot, models);
            }

            return models.ToArray();
        }

        [Route("api/functions/definitions")]
        public IHttpActionResult GetFunctionDefinitions([FromUri]PagingInfo pagingInfo)
        {
            if (pagingInfo == null)
            {
                throw new ArgumentNullException("pagingInfo");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            IResultSegment<FunctionIndexEntry> indexSegment = _functionIndexReader.Read(pagingInfo.Limit,
                pagingInfo.ContinuationToken);

            var model = new FunctionStatisticsSegment();
            bool foundItem;

            if (indexSegment != null && indexSegment.Results != null)
            {
                IEnumerable<FunctionStatisticsViewModel> query = indexSegment.Results
                    .Select(entry => new FunctionStatisticsViewModel
                    {
                        FunctionId = entry.Id,
                        FunctionFullName = entry.FullName,
                        FunctionName = entry.ShortName,
                        IsRunning = HostHasHeartbeat(entry).GetValueOrDefault(true),
                        FailedCount = 0,
                        SuccessCount = 0
                    });

                model.Entries = query.ToArray();
                foundItem = query.Any();
                model.ContinuationToken = indexSegment.ContinuationToken;
            }
            else
            {
                foundItem = false;
            }

            if (pagingInfo.ContinuationToken == null)
            {
                // For requests for the first page only, also provide a version to enable polling for updates.
                model.VersionUtc = _functionIndexReader.GetCurrentVersion().UtcDateTime;
            }

            model.StorageAccountName = _account.Credentials.AccountName;

            if (!foundItem)
            {
                model.IsOldHost = OnlyBeta1HostExists(alreadyFoundNoNewerEntries: true);
            }

            if (model.Entries != null)
            {
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
            }

            return Ok(model);
        }

        [Route("api/functions/newerDefinitions")]
        public IHttpActionResult GetNewerFunctionDefinition(DateTime version)
        {
            DateTimeOffset latestVersion = _functionIndexReader.GetCurrentVersion();
            bool newerExists = latestVersion.UtcDateTime > version.ToUniversalTime();
            return Ok(newerExists);
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

        private bool? HostHasHeartbeat(FunctionIndexEntry function)
        {
            if (!function.HeartbeatExpirationInSeconds.HasValue)
            {
                return null;
            }

            return HostHasHeartbeat(function.HeartbeatSharedContainerName, function.HeartbeatSharedDirectoryName,
                function.HeartbeatExpirationInSeconds.Value);
        }

        internal static bool? HostHasHeartbeat(IHeartbeatValidityMonitor heartbeatMonitor, FunctionSnapshot snapshot)
        {
            return HostHasHeartbeat(heartbeatMonitor, snapshot.HeartbeatExpirationInSeconds,
                snapshot.HeartbeatSharedContainerName, snapshot.HeartbeatSharedDirectoryName);
        }

        private static bool? HostHasHeartbeat(IHeartbeatValidityMonitor heartbeatMonitor, int? expirationInSeconds,
            string containerName, string directoryName)
        {
            if (!expirationInSeconds.HasValue)
            {
                return null;
            }

            return heartbeatMonitor.IsSharedHeartbeatValid(containerName, directoryName, expirationInSeconds.Value);
        }

        private bool? HostHasHeartbeat(string sharedContainerName, string sharedDirectoryName, int expirationInSeconds)
        {
            string hostId = sharedContainerName + "/" + sharedDirectoryName;

            if (_cachedHostHeartbeats.ContainsKey(hostId))
            {
                return _cachedHostHeartbeats[hostId];
            }
            else
            {
                bool? heartbeat = _heartbeatMonitor.IsSharedHeartbeatValid(sharedContainerName, sharedDirectoryName,
                    expirationInSeconds);
                _cachedHostHeartbeats.AddOrUpdate(hostId, heartbeat, (ignore1, ignore2) => heartbeat);
                return heartbeat;
            }
        }

        private bool? HostInstanceHasHeartbeat(FunctionInstanceSnapshot snapshot)
        {
            HeartbeatDescriptor heartbeat = snapshot.Heartbeat;

            if (heartbeat == null)
            {
                return null;
            }

            return HostInstanceHasHeartbeat(heartbeat.SharedContainerName, heartbeat.SharedDirectoryName,
                heartbeat.InstanceBlobName, heartbeat.ExpirationInSeconds);
        }

        private bool? HostInstanceHasHeartbeat(string sharedContainerName, string sharedDirectoryName,
            string instanceBlobName, int expirationInSeconds)
        {
            return _heartbeatMonitor.IsInstanceHeartbeatValid(sharedContainerName, sharedDirectoryName,
                instanceBlobName, expirationInSeconds);
        }
    }
}
