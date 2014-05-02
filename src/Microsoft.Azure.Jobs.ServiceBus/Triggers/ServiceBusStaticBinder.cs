using System;
using System.Reflection;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs
{
    internal class ServiceBusStaticBinder
    {
        static ParameterStaticBinding Bind(ServiceBusAttribute attr, ParameterInfo parameter)
        {
            var entityPath = parameter.Name;
            if (!String.IsNullOrEmpty(attr.EntityName))
            {
                entityPath = attr.EntityName;
            }
            else if (!String.IsNullOrEmpty(attr.Subscription))
            {
                entityPath = SubscriptionClient.FormatSubscriptionPath(attr.Topic, attr.Subscription);
            }

            string[] namedParams = QueueInputParameterRuntimeBinding.GetRouteParametersFromParamType(parameter.ParameterType);

            return new ServiceBusParameterStaticBinding
            {
                EntityPath = entityPath,
                IsInput = !parameter.IsOut,
                Params = namedParams
            };
        }
    }
}