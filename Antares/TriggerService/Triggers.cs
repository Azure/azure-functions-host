using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace TriggerService
{
    public enum TriggerType
    {
        Blob,
        Queue,
        Timer
    }


    public static partial class Utility
    {
        // Hook to help deal with polymorphism. 
        public static Trigger[] DeserializeTriggerArray(string json)
        {
            var ts2 = JsonConvert.DeserializeObject<TriggerRaw[]>(json);
            Trigger[] ts3 = Array.ConvertAll(ts2, x => x.Convert());
            return ts3;
        }
    }

    // Serialization helper to deal with polymorphism.
    // $$$ Is there some Json.Net hook to make this go away?
    public class TriggerRaw
    {
        // Invoke this path when the trigger fires 
        public string CallbackPath { get; set; }
        public string AccountConnectionString { get; set; } // $$$

        public TriggerType Type { get; set; }

        // Blob path
        public string BlobInput { get; set; }
        public string BlobOutput { get; set; }


        // Queue 
        public string QueueName { get; set; }

        // Timer
        public TimeSpan Interval { get; set; }

        public Trigger Convert()
        {
            switch (this.Type)
            {
                case TriggerType.Blob:
                    return new BlobTrigger
                    {
                        CallbackPath = this.CallbackPath,
                        AccountConnectionString = this.AccountConnectionString,
                        BlobInput = this.BlobInput,
                        BlobOutput = this.BlobOutput
                    };
                case TriggerType.Queue:
                    return new QueueTrigger
                    {
                        CallbackPath = this.CallbackPath,
                        AccountConnectionString = this.AccountConnectionString,
                        QueueName = this.QueueName
                    };
                case TriggerType.Timer:
                    return new TimerTrigger
                    {
                        CallbackPath = this.CallbackPath,
                        AccountConnectionString = this.AccountConnectionString,
                        Interval = this.Interval
                    };
                default:
                    throw new InvalidOperationException("Unknown Trigger type:" + this.Type);
            }
        }
    }

    // $$$ Blob listener, Queue listener, Cron?
    public abstract class Trigger
    {
        // Invoke this path when the trigger fires 
        public string CallbackPath { get; set; }

        // Not serialized. For in-memory cases.
        public object Tag { get; set; }

        // $$$ Need abstraction here, may get via antares instead. 
        public string AccountConnectionString { get; set; }

        public TriggerType Type { get; set; }
    }

    public class BlobTrigger : Trigger
    {
        public BlobTrigger()
        {
            this.Type = TriggerType.Blob;
        }

        public string BlobInput { get; set; }

        // Semicolon separated list of output blobs. 
        // Don't fire trigger if all ouptuts are newer than the input. 
        public string BlobOutput { get; set; }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Trigger on {0}", BlobInput);
            if (BlobOutput != null)
            {
                sb.AppendFormat(" unless {0} is newer", BlobOutput);
            }
            return sb.ToString();
        }
    }

    public class QueueTrigger : Trigger
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

    public class TimerTrigger : Trigger
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