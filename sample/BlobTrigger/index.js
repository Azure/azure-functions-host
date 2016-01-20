module.exports = function (context) {
    console.log('Node.js blob trigger function processed work item ' + context.workItem.id);
    context.done();
}