module.exports = function (workItem, context) {
    context.log('Node.js eventhub trigger function processed work item ' + workItem.id);

    context.done(null, workItem);
}