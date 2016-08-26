var util = require('util');

module.exports = function (context, req) {
    var scenario = req.body.scenario;

    if (scenario == "echo") {
        context.res = req.body.value;
    }
    else {
        context.res = {
            status: 400
        };
    }

    context.done();
}