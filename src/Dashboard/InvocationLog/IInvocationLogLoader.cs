using System;
using System.Collections.Generic;
using Dashboard.ViewModels;

namespace Dashboard.InvocationLog
{
    internal interface IInvocationLogLoader
    {
        InvocationLogPage GetInvocationChildren(Guid invocationId, PagingInfo pagingInfo);

        InvocationLogViewModel[] GetInvocationsByIds(IEnumerable<Guid> invocationIds);
    }
}
