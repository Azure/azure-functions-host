module.exports = function (context, workItem) {
    context.log('Node.js queue trigger function processed work item ' + workItem.id);

    context.log('DequeueCount=' + context.bindingData.DequeueCount);
    context.log('InsertionTime=' + context.bindingData.InsertionTime);

    context.done(null, {
        receipt: workItem
    });
}