module.exports = function (context) {
    var workItem = context.input;
    console.log('Node.js blob trigger function processed work item ' + workItem.ID);
    context.done();
}