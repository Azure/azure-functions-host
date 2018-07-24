var util = require('util');

module.exports = function (context, input) {
    var parsedInput = JSON.parse(input);
    context.log(util.format("Node.js script processed %d events", parsedInput.length));
    context.done();
}