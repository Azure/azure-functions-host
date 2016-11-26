var util = require('util');

module.exports = function (context, req) {
    var scenario = req.body.scenario;
    
    if (scenario == "echo") {
        context.res = req.body.value;
    }
    else if (scenario == "rawresponse") {
        context.res = {
            status: 200,
            body: req.body.value,
            headers: {
                'Content-Type': req.body.contenttype
            },
            isRaw: true
        }
    }
    else if (scenario == "rawresponsenocontenttype") {
        context.res = {
            status: 200,
            body: req.body.value,
            isRaw: true
        }
    }
    else {
        context.res = {
            status: 400
        };
    }

    context.done();
}