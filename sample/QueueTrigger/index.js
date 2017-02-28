module.exports = function (context, workItem) {
    context.log('Node.js queue trigger function processed work item', workItem.id);

    context.log('DequeueCount=%s', context.bindingData.dequeueCount);
    context.log('InsertionTime=%s', context.bindingData.insertionTime);

    context.done(null, workItem);
}