module.exports = function (context, workItem) {
    context.log('Node.js queue trigger function processed work item ' + workItem.id);
    context.done(null, {
        receipt: workItem
    });
}