module.exports = function (context) {
    var message = context.message;
    context.log("Node.js ServiceBus queue trigger function processed message '" + JSON.stringify(message) + "'");

    if (message.count < 1)
    {
        // write a message back to the queue that this function is triggered on
        // ensuring that we only loop on this once
        message.count += 1;
        context.output({
            message: JSON.stringify(message)
        });
    }

    context.done();
}