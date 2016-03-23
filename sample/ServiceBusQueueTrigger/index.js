module.exports = function (context, message) {
    context.log('Node.js ServiceBus queue trigger function processed message', message);

    var result = null;
    if (message.count < 1)
    {
        // write a message back to the queue that this function is triggered on
        // ensuring that we only loop on this once
        message.count += 1;
        result = {
            message: JSON.stringify(message)
        };
    }
    context.bindings.output = result;

    context.done();
}