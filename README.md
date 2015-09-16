Azure WebJobs SDK Script
===
This repo contains libraries that enable a light-weight scripting model for the [Azure WebJobs SDK](http://github.com/Azure/azure-webjobs-sdk). You provide job function scripts written in various languages (e.g. Node.js, CSharp, etc.) along with a simple manifest.json file that indicates how those functions should be invoked, and the scripting library does the work necessary to plug those scripts into the WebJobs SDK JobHost.

This opens the door for UI driven scenarios, where the user simply chooses some trigger options from dropdowns, provides a job script, and the UI generates the correstponding manifest.json for the selections. The scripting runtime is able to take those two simple inputs and set everything else up.

As an example, here's a simple Node.js job function that processes a work item:

    function processWorkItem(workItem, callback) {
        console.log('Work Item processed: ' + workItem.ID);
        callback();
    }

And here is the corresponding manifest.json file that directs the runtime to invoke this function whenever a new queue message is added to the 'samples-workitems' Azure Storage Queue:

    {
      "functions": [
        {
          "name": "ProcessWorkItem",
          "source": "processWorkItem.js",
          "trigger": {
          "type": "queue",
          "queueName": "samples-workitems"
        }
      }
    ]
  }
