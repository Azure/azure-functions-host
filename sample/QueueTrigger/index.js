module.exports = function (context) {
    context.log('Node.js queue trigger function processed work item ' + context.workItem.id);

    context.done(null, context.workItem);
}