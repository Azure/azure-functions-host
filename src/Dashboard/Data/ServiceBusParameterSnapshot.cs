using System;
using System.Globalization;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Data
{
    [JsonTypeName("ServiceBus")]
    internal class ServiceBusParameterSnapshot : ParameterSnapshot
    {
        public string EntityPath { get; set; }

        public bool IsInput { get; set; }

        public override string Description
        {
            get
            {
                if (this.IsInput)
                {
                    return String.Format(CultureInfo.CurrentCulture, "dequeue from '{0}'", this.EntityPath);
                }
                else
                {
                    return String.Format(CultureInfo.CurrentCulture, "enqueue to '{0}'", this.EntityPath);
                }
            }
        }

        public override string AttributeText
        {
            get { return String.Format(CultureInfo.CurrentCulture, "[ServiceBus(\"{0}\")]", EntityPath); }
        }

        public override string Prompt
        {
            get
            {
                if (IsInput)
                {
                    return "Enter the queue message body";
                }
                else
                {
                    return "Enter the output entity name";
                }
            }
        }

        public override string DefaultValue
        {
            get
            {
                if (IsInput)
                {
                    return null;
                }
                else
                {
                    return EntityPath;
                }
            }
        }
    }
}
