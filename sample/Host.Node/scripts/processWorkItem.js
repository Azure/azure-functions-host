module.exports = function (context, callback) {
    var workItem = context.input;
    console.log('Node.js job function processed work item ' + workItem.ID);
    callback();
}