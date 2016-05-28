var util = require('util');

module.exports = function (context, message) {
    context.log('Node.js ServiceBus queue trigger function processed message', message);

    if (message.count < message.max)
    {
        // write a message back to the queue that this function is triggered
        message.count += 1;
        context.bindings.message = message;
    }
    else {
        context.bindings.output = util.format('%d messages processed', message.count);
    }

    context.done();
}