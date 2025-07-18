module.exports = function (context, workItem) {
    context.log('Node.js eventhub trigger function processed work item', workItem.id);

    context.log('Sequence Number: ', context.bindingData.sequenceNumber);
    context.log('Enqueued Time: ', context.bindingData.enqueuedTimeUtc);

    var result = {
        id: workItem.id,
        bindingData: context.bindingData
    };
    context.done(null, result);
}