module.exports = function (context, inMessage) {
    var bindingData = context.bindingData;
    context.log('Message Id: ', bindingData.messageId);
    context.log('Delivery count: ', bindingData.deliveryCount);
    context.log('Enqueued time: ', bindingData.enqueuedTimeUtc);

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