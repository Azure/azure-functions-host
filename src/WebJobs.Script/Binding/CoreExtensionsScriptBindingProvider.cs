// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using System;
using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.Logging;
using NCrontab;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    /// <summary>
    /// BindingProvider for the core WebJobs Extensions
    /// </summary>
    internal class CoreExtensionsScriptBindingProvider : ScriptBindingProvider
    {
        public CoreExtensionsScriptBindingProvider(ILogger logger)
            : base(logger)
        {
        }

        /// <inheritdoc/>
        public override bool TryCreate(ScriptBindingContext context, out ScriptBinding binding)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            binding = null;

            if (string.Compare(context.Type, "timerTrigger", StringComparison.OrdinalIgnoreCase) == 0)
            {
                binding = new TimerTriggerScriptBinding(context);
            }

            return binding != null;
        }

        //TODO: DI(FACAVAL) Re-add when we migrate the timer extension
        internal class TimerTriggerScriptBinding : ScriptBinding
        {
            public TimerTriggerScriptBinding(ScriptBindingContext context) : base(context)
            {
            }

            public override Type DefaultType
            {
                get
                {
                    // TODO: DI (FACAVAL) Fix. Timer needs to be migrated and referenced.
                    return typeof(object);
                    //return typeof(TimerInfo);
                }
            }

            public override Collection<Attribute> GetAttributes()
            {
                Collection<Attribute> attributes = new Collection<Attribute>();

                string schedule = Context.GetMetadataValue<string>("schedule");
                bool runOnStartup = Context.GetMetadataValue<bool>("runOnStartup");
                bool useMonitor = Context.GetMetadataValue<bool>("useMonitor", true);

                if (Utility.IsDynamic)
                {
                    // pre-resolve app setting specifiers
                    var resolver = new DefaultNameResolver();
                    schedule = resolver.ResolveWholeString(schedule);

                    var options = new CrontabSchedule.ParseOptions()
                    {
                        IncludingSeconds = true
                    };
                    if (CrontabSchedule.TryParse(schedule, options) == null)
                    {
                        throw new ArgumentException(string.Format("'{0}' is not a valid CRON expression.", schedule));
                    }
                }

                // TODO: DI (FACAVAL) Fix this once timer is migrated and referenced
                //attributes.Add(new TimerTriggerAttribute(schedule)
                //{
                //    RunOnStartup = runOnStartup,
                //    UseMonitor = useMonitor
                //});

                return attributes;
            }
        }
    }
}