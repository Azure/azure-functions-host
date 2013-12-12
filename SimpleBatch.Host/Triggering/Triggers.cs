using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using TriggerService.Internal;

namespace TriggerService
{
    // Base class for triggers that client can listen on. 
    internal abstract class Trigger
    {
        // Invoke this path when the trigger fires 
        public string CallbackPath { get; set; }

        // Not serialized. For in-memory cases.(This is kind of exclusive with CallbackPath)
        public object Tag { get; set; }

        // $$$ Need abstraction here, may get via antares instead. 
        public string AccountConnectionString { get; set; }

        public TriggerType Type { get; set; }

        public static IEnumerable<Trigger> FromWire(IEnumerable<TriggerRaw> raw, Credentials credentials)
        {
            return from x in raw select FromWire(x, credentials);
        }

        public static Trigger FromWire(TriggerRaw raw, Credentials credentials)
        {
            switch (raw.Type)
            {
                case TriggerType.Blob:
                    var trigger = new BlobTrigger
                    {
                        CallbackPath = raw.CallbackPath,
                        AccountConnectionString = credentials.AccountConnectionString,
                        BlobInput = new CloudBlobPath(raw.BlobInput)
                    };
                    if (raw.BlobOutput != null)
                    {
                        string[] parts = raw.BlobOutput.Split(';');
                        trigger.BlobOutputs = Array.ConvertAll(parts, part => new CloudBlobPath(part.Trim()));                       
                    }
                    return trigger;
                case TriggerType.Queue:
                    return new QueueTrigger
                    {
                        CallbackPath = raw.CallbackPath,
                        AccountConnectionString = credentials.AccountConnectionString,
                        QueueName = raw.QueueName
                    };
                case TriggerType.Timer:
                    return new TimerTrigger
                    {
                        CallbackPath = raw.CallbackPath,
                        Interval = raw.Interval.Value
                    };
                default:
                    throw new InvalidOperationException("Unknown Trigger type:" + raw.Type);
            }
        }
    }

    internal class BlobTrigger : Trigger
    {
        public BlobTrigger()
        {
            this.Type = TriggerType.Blob;
        }

        public CloudBlobPath BlobInput { get; set; }

        // list of output blobs. Null if no outputs. 
        // Don't fire trigger if all ouptuts are newer than the input. 
        public CloudBlobPath[] BlobOutputs { get; set; }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Trigger on {0}", BlobInput);
            if (BlobOutputs != null)
            {
                sb.AppendFormat(" unless {0} is newer", string.Join<CloudBlobPath>(";", BlobOutputs));
            }
            return sb.ToString();
        }
    }

    internal class QueueTrigger : Trigger
    {
        public QueueTrigger()
        {
            this.Type = TriggerType.Queue;
        }

        public string QueueName { get; set; }

        public override string ToString()
        {
            return string.Format("Trigger on queue {0}", QueueName);
        }
    }

    internal class TimerTrigger : Trigger
    {
        public TimerTrigger()
        {
            this.Type = TriggerType.Timer;
        }
        public TimeSpan Interval { get; set; }

        public override string ToString()
        {
            return string.Format("Trigger on {0} interval", Interval);
        }
    }
}