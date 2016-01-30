module.exports = function (workItem, context) {
    context.log('Node.js queue trigger function processed work item ' + workItem.id);

    context.done(null, workItem);
}