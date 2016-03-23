module.exports = function (context, workItem) {
    context.log('Node.js queue trigger function processed work item', workItem.id);

    context.log('DequeueCount=%s', context.bindingData.DequeueCount);
    context.log('InsertionTime=%s', context.bindingData.InsertionTime);

    context.done(null, {
        receipt: workItem
    });
}