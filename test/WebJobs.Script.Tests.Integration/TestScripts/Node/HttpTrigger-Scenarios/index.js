var util = require('util');

module.exports = function (context, req) {
    var scenario = (req.headers && req.headers.scenario) || req.body.scenario;
    
    if (scenario == "echo") {
        context.res = req.body.value;
    }
    else if (scenario == "buffer")
    {
        context.res.send(Buffer.from('0001', 'hex'));
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
    else if (scenario == "content") {
        if (req.headers.return) {
            context.res = req.body;
            context.done();
        } else {
            var sendFunc = req.headers.raw ? 'raw' : 'send';
            context.res.type(req.headers.type)[sendFunc](req.body);
        }
    }
    else {
        context.res = {
            status: 400
        };
    }

    context.done();
}