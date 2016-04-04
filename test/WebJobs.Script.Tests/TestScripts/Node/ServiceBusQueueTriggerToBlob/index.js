module.exports = function (context, message) {
    if (message.count < 2)
    {
        // write a message back to the queue that this function is triggered on
        // to trigger it again
        message.count += 1;
        context.bindings.message = message;
    }
    else {
        // write final result blob
        context.bindings.completed = message.id;
    }

    context.done();
}