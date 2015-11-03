var util = require('util');

module.exports = function (context) {
    var workItem = context.input;
    context.log('Node.js queue trigger function processed work item ' + util.inspect(workItem.ID));
    context.done();
}