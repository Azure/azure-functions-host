// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using Dashboard.Data;
using Dashboard.Data.Logs;
using Dashboard.ViewModels;
using Microsoft.Azure.WebJobs.Protocols;
using Dashboard.Indexers;

namespace Dashboard.ApiControllers
{
    public class DiagnosticsController : ApiController
    {
        private readonly IPersistentQueueReader<PersistentQueueMessage> _queueReader;
        private readonly IIndexerLogReader _indexerLogReader;
        private readonly IDashboardVersionManager _dashboardVersionManager;

        public DiagnosticsController(IIndexerLogReader indexerLogReader,
            IPersistentQueueReader<PersistentQueueMessage> queueReader,
            IDashboardVersionManager dashboardVersionManager)
        {
            if (indexerLogReader == null)
            {
                throw new ArgumentNullException("indexerLogReader");
            }
            if (queueReader == null)
            {
                throw new ArgumentNullException("queueReader");
            }
            if (dashboardVersionManager == null)
            {
                throw new ArgumentNullException("dashboardVersionManager");
            }

            _indexerLogReader = indexerLogReader;
            _queueReader = queueReader;
            _dashboardVersionManager = dashboardVersionManager;
        }

        [HttpGet]
        [Route("api/diagnostics/indexingQueueLength/{limit?}")]
        public IHttpActionResult IndexingQueueLength(int? limit = null)
        {
            return Ok(_queueReader.Count(limit));
        }

        [HttpGet]
        [Route("api/diagnostics/indexerLogEntry")]
        public IHttpActionResult IndexerLogEntry([FromUri] string entryId)
        {
            if (string.IsNullOrEmpty(entryId))
            {
                return BadRequest();
            }

            return GetIndexerLogEntry(entryId);
        }

        [HttpGet]
        [Route("api/diagnostics/upgradeStatus")]
        public IHttpActionResult UpgradeStatus()
        {
            if (_dashboardVersionManager.CurrentVersion == null)
            {
                _dashboardVersionManager.CurrentVersion = _dashboardVersionManager.Read();
            }

            return Ok(_dashboardVersionManager.CurrentVersion.Document);
        }

        [HttpGet]
        [Route("api/diagnostics/indexerLogs")]
        public IHttpActionResult IndexerLogs([FromUri]PagingInfo pagingInfo)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (pagingInfo == null)
            {
                return BadRequest();
            }

            return GetIndexerLogs(pagingInfo);
        }

        private IHttpActionResult GetIndexerLogEntry(string entryId)
        {
            IndexerLogEntry logEntry = _indexerLogReader.ReadWithDetails(entryId);
            if (logEntry == null)
            {
                return NotFound();
            }

            return Ok(logEntry);
        }

        private IHttpActionResult GetIndexerLogs(PagingInfo pagingInfo)
        {
            IResultSegment<IndexerLogEntry> logs = _indexerLogReader.ReadWithoutDetails(
                pagingInfo.Limit,
                pagingInfo.ContinuationToken);

            IndexerLogEntriesViewModel viewModel = new IndexerLogEntriesViewModel();

            if (logs != null)
            {
                viewModel.Entries = logs.Results;
                viewModel.ContinuationToken = logs.ContinuationToken;
            }

            return Ok(viewModel);
        }
    }
}
