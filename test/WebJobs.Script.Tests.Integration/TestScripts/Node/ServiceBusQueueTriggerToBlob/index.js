module.exports = function (context, inMessage) {
    if (inMessage.count < 2)
    {
        // write a message back to the queue that this function is triggered on
        // to trigger it again
        inMessage.count += 1;
        context.bindings.outMessage = inMessage;
    }
    else {
        // write final result blob
        context.bindings.blob = inMessage.id;
    }

    context.done();
}