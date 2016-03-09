module.exports = function (context, workItem) {
    console.log('Node.js blob trigger function processed work item ' + workItem.id);
    context.done();
}