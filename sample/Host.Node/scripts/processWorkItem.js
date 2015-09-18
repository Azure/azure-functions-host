module.exports = function (context) {
    var workItem = context.input;
    console.log('Node.js job function processed work item ' + workItem.ID);
    context.done();
}