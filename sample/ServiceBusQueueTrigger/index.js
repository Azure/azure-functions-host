module.exports = function (context, message) {
    context.log('Node.js ServiceBus queue trigger function processed message', message);

    if (message.count < 2)
    {
        // write a message back to the queue that this function is triggered on
        // ensuring that we only loop on this twice
        message.count += 1;
        context.bindings.message = message;
    }

    context.done();
}