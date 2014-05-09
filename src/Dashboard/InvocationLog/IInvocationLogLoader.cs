using System;
using System.Collections.Generic;
using Dashboard.ViewModels;

namespace Dashboard.InvocationLog
{
    internal interface IInvocationLogLoader
    {
        InvocationLogPage GetInvocationChildren(Guid invocationId, PagingInfo pagingInfo);

        InvocationLogPage GetRecentInvocations(PagingInfo pagingInfo);

        InvocationLogPage GetInvocationsInFunction(string functionId, PagingInfo pagingInfo);

        InvocationLogPage GetInvocationsInJob(string jobKey, PagingInfo pagingInfo);

        InvocationLogViewModel[] GetInvocationsByIds(IEnumerable<Guid> invocationIds);

        DateTimeOffset? GetHeartbeat(Guid hostInstanceId);
    }
}