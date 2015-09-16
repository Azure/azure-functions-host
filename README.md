Azure WebJobs SDK Script
===
This repo contains libraries that enable a light-weight scripting model for the [Azure WebJobs SDK](http://github.com/Azure/azure-webjobs-sdk). You just provide job function scripts written in various languages (e.g. Javascript/[Node.js](http://nodejs.org), CSharp, etc.) along with a simple **manifest.json** file that indicates how those functions should be invoked, and the scripting library does the work necessary to plug those scripts into the WebJobs SDK JobHost runtime.

This opens the door for **UI driven scenarios**, where the user simply chooses trigger options from drop-downs, provides a job script, and the correstponding manifest.json is generated behind the scenes. The scripting runtime is able to take these two simple inputs and set everything else up.

As an example, here's a simple Node.js job function in a processWorkItem.js file:

```javascript
    function processWorkItem(workItem, callback) {
        console.log('Work Item processed: ' + workItem.ID);
        callback();
    }
```

And here's the corresponding manifest.json file that instructs the runtime to invoke this function whenever a new queue message is added to the 'samples-workitems' Azure Storage Queue:

```javascript
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
```
That's all that is required from the user! The runtime will be initialized automatically with these inputs and live monitoring of the Azure Queue will begin, and the function will be invoked when queue messages are added.
