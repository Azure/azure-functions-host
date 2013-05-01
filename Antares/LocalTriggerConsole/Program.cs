using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TriggerService
{
    class Program
    {
        static void Main()
        {
            ITriggerMap map = new TriggerMap();
            map.AddTriggers("scope", new QueueTrigger
            {
                AccountConnectionString = "???",
                CallbackPath = "http://foo",
                QueueName = "myqueue",
            });

            var l = new Listener(map, new WebInvoke());

            l.Poll();
        }
    }        

}
