var util = require('util');

module.exports = function (context) {
    var json = JSON.stringify(context.input);
    context.log("Node.js script processed queue message '" + json + "'");

    context.output({
        output: json
    });

    context.done();
}