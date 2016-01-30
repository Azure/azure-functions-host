module.exports = function (workItem, context) {
    console.log('Node.js blob trigger function processed work item ' + workItem.id);
    context.done();
}