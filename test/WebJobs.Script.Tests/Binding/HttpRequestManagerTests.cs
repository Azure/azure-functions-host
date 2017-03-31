// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding.Http;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Binding
{
    public class HttpRequestManagerTests
    {
        private TestTraceWriter _traceWriter;

        public HttpRequestManagerTests()
        {
            _traceWriter = new TestTraceWriter(TraceLevel.Verbose);
        }

        [Fact]
        public async Task ProcessRequest_PropigatesExceptions()
        {
            var ex = new Exception("Kaboom!");
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> process = (req, ct) =>
            {
                throw ex;
            };
            var config = new HttpConfiguration
            {
                MaxQueueLength = 10,
                MaxDegreeOfParallelism = 5
            };
            var manager = new HttpRequestManager(config, _traceWriter);

            var resultEx = await Assert.ThrowsAsync<Exception>(async () =>
            {
                var request = new HttpRequestMessage();
                await manager.ProcessRequestAsync(request, process, CancellationToken.None);
            });
            Assert.Same(ex, resultEx);
        }

        [Fact]
        public async Task ProcessRequest_NoThrottle_DispatchesDirectly()
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> process = (req, ct) =>
            {
                return Task.FromResult(response);
            };
            var config = new HttpConfiguration();
            var manager = new HttpRequestManager(config, _traceWriter);

            var request = new HttpRequestMessage();
            var result = await manager.ProcessRequestAsync(request, process, CancellationToken.None);
            Assert.Same(response, result);
        }

        [Fact]
        public async Task ProcessRequest_MaxParallelism_RequestsAreThrottled()
        {
            int maxParallelism = 3;
            int count = 0;
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> process = async (req, ct) =>
            {
                if (Interlocked.Increment(ref count) > maxParallelism)
                {
                    throw new Exception("Kaboom!");
                }

                await Task.Delay(100);

                Interlocked.Decrement(ref count);

                return new HttpResponseMessage(HttpStatusCode.OK);
            };
            var config = new HttpConfiguration
            {
                MaxDegreeOfParallelism = maxParallelism
            };
            var manager = new HttpRequestManager(config, _traceWriter);

            // expect all requests to succeed
            var tasks = new List<Task<HttpResponseMessage>>();
            for (int i = 0; i < 20; i++)
            {
                var request = new HttpRequestMessage();
                tasks.Add(manager.ProcessRequestAsync(request, process, CancellationToken.None));
            }
            await Task.WhenAll(tasks);
            Assert.True(tasks.All(p => p.Result.StatusCode == HttpStatusCode.OK));
        }

        [Fact]
        public async Task ProcessRequest_MaxQueueLength_RequestsAreRejected()
        {
            int maxQueueLength = 10;
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> process = async (req, ct) =>
            {
                await Task.Delay(100);
                return new HttpResponseMessage(HttpStatusCode.OK);
            };
            var config = new HttpConfiguration
            {
                MaxQueueLength = maxQueueLength,
                MaxDegreeOfParallelism = 1
            };
            var manager = new HttpRequestManager(config, _traceWriter);

            // expect requests past the threshold to be rejected
            var tasks = new List<Task<HttpResponseMessage>>();
            for (int i = 0; i < 25; i++)
            {
                var request = new HttpRequestMessage();
                tasks.Add(manager.ProcessRequestAsync(request, process, CancellationToken.None));
            }
            await Task.WhenAll(tasks);
            int countSuccess = tasks.Count(p => p.Result.StatusCode == HttpStatusCode.OK);
            Assert.Equal(maxQueueLength, countSuccess);
            int rejectCount = 25 - countSuccess;
            Assert.Equal(rejectCount, tasks.Count(p => p.Result.StatusCode == (HttpStatusCode)429));

            Assert.Equal(rejectCount, _traceWriter.Traces.Count);
            Assert.True(_traceWriter.Traces.All(p => string.Compare("Http request queue limit of 10 has been exceeded.", p.Message) == 0));

            // send a number of requests not exceeding the limit
            // expect all to succeed
            tasks = new List<Task<HttpResponseMessage>>();
            for (int i = 0; i < maxQueueLength; i++)
            {
                var request = new HttpRequestMessage();
                tasks.Add(manager.ProcessRequestAsync(request, process, CancellationToken.None));
            }
            await Task.WhenAll(tasks);
            Assert.True(tasks.All(p => p.Result.StatusCode == HttpStatusCode.OK));
        }

        [Fact]
        public async Task ProcessRequest_HostIsOverloaded_RequestsAreRejected()
        {
            bool rejectRequests = false;
            var config = new HttpConfiguration();
            Func<bool> rejectAllRequests = () =>
            {
                return rejectRequests;
            };

            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> process = async (req, ct) =>
            {
                await Task.Delay(100);
                return new HttpResponseMessage(HttpStatusCode.OK);
            };
            var manager = new TestHttpRequestManager(config, _traceWriter, rejectAllRequests);

            var tasks = new List<Task<HttpResponseMessage>>();
            for (int i = 0; i < 10; i++)
            {
                if (i == 7)
                {
                    rejectRequests = true;
                }
                var request = new HttpRequestMessage();
                tasks.Add(manager.ProcessRequestAsync(request, process, CancellationToken.None));
            }
            await Task.WhenAll(tasks);

            Assert.Equal(7, tasks.Count(p => p.Result.StatusCode == HttpStatusCode.OK));
            Assert.Equal(3, tasks.Count(p => p.Result.StatusCode == (HttpStatusCode)429));
        }

        [Fact]
        public async Task ProcessRequest_HostIsOverloaded_CustomRejectAction()
        {
            bool rejectOverrideCalled = false;
            var config = new HttpConfiguration();
            Func<bool> rejectAllRequests = () =>
            {
                return true;
            };
            Func<HttpRequestMessage, HttpResponseMessage> rejectRequest = (req) =>
            {
                rejectOverrideCalled = true;
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            };

            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> process = (req, ct) =>
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            };
            var manager = new TestHttpRequestManager(config, _traceWriter, rejectAllRequests, rejectRequest);

            var request = new HttpRequestMessage();
            var response = await manager.ProcessRequestAsync(request, process, CancellationToken.None);

            Assert.True(rejectOverrideCalled);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        private class TestHttpRequestManager : HttpRequestManager
        {
            private readonly Func<bool> _rejectAllRequests;
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _rejectRequest;

            public TestHttpRequestManager(HttpConfiguration config, TraceWriter traceWriter, Func<bool> rejectAllRequests = null, Func<HttpRequestMessage, HttpResponseMessage> rejectRequest = null) : base(config, traceWriter)
            {
                _rejectAllRequests = rejectAllRequests;
                _rejectRequest = rejectRequest;
            }

            protected override bool RejectAllRequests()
            {
                if (_rejectAllRequests != null)
                {
                    return _rejectAllRequests();
                }
                return base.RejectAllRequests();
            }

            protected override HttpResponseMessage RejectRequest(HttpRequestMessage request)
            {
                if (_rejectRequest != null)
                {
                    return _rejectRequest(request);
                }
                return base.RejectRequest(request);
            }
        }
    }
}
