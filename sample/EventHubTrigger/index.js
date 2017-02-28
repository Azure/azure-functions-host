var util = require('util');

module.exports = function (context, input) {
    context.log(util.format("Node.js script processed %d events", input.length));
    context.log("IsArray", util.isArray(input));

    for (i = 0; i < input.length; i++)
    {
        context.log(input[i].value);
    }

    context.done();
}