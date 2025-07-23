var util = require('util');

module.exports = function (context, input) {
    context.log(util.format("Node.js script processed %d events", input.length));
    context.done();
}