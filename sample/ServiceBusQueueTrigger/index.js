var util = require('util');

module.exports = function (context, inMessage) {
    context.log('Node.js ServiceBus queue trigger function processed message', inMessage);

    if (inMessage.count < inMessage.max)
    {
        // write a message back to the queue that this function is triggered
        inMessage.count += 1;
        context.bindings.outMessage = inMessage;
    }
    else {
        context.bindings.blob = util.format('%d messages processed', inMessage.count);
    }

    context.done();
}