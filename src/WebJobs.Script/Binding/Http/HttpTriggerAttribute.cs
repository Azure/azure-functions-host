using System;

namespace Microsoft.Azure.WebJobs.Script.Binding.Http
{
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class HttpTriggerAttribute : Attribute
    {
    }
}
