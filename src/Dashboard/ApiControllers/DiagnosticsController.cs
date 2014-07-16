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

namespace Dashboard.ApiControllers
{
    public class DiagnosticsController : ApiController
    {
        private readonly IIndexerLogReader _indexerLogReader;

        public DiagnosticsController(IIndexerLogReader indexerLogReader)
        {
            if (indexerLogReader == null)
            {
                throw new ArgumentNullException("indexerLogReader");
            }

            _indexerLogReader = indexerLogReader;
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
