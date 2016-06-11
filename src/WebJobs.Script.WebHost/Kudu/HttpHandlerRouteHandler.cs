using System;
using System.Web;
using System.Web.Routing;
using Autofac;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    public class HttpHandlerRouteHandler : IRouteHandler
    {
        private readonly Type _handlerType;
        private readonly IContainer _container;

        public HttpHandlerRouteHandler(IContainer container, Type handlerType)
        {
            _container = container;
            _handlerType = handlerType;
        }

        public IHttpHandler GetHttpHandler(RequestContext requestContext)
        {
            return (IHttpHandler)_container.Resolve(_handlerType);
        }
    }
}