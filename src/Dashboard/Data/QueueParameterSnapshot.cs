// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using Microsoft.Azure.WebJobs.Protocols;

namespace Dashboard.Data
{
    [JsonTypeName("Queue")]
    internal class QueueParameterSnapshot : ParameterSnapshot
    {
        public string QueueName { get; set; }

        public bool IsInput { get; set; }

        public override string Description
        {
            get
            {
                if (this.IsInput)
                {
                    return String.Format(CultureInfo.CurrentCulture, "dequeue from '{0}'", this.QueueName);
                }
                else
                {
                    return String.Format(CultureInfo.CurrentCulture, "enqueue to '{0}'", this.QueueName);
                }
            }
        }

        public override string AttributeText
        {
            get { return String.Format(CultureInfo.CurrentCulture, "[Queue(\"{0}\")]", QueueName); }
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
                    return "Enter the output queue name";
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
                    return QueueName;
                }
            }
        }
    }
}
