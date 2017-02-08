var util = require('util');

module.exports = function (context, req) {
    var scenario = (req.headers && req.headers.scenario) || req.body.scenario;
    
    switch (scenario) {
        case "echo":
            context.res = req.body.value;
            break;

        case "buffer":
            context.res.send(Buffer.from('0001', 'hex'));
            break;

        case "rawresponse":
            context.res = {
                status: 200,
                body: req.body.value,
                headers: {
                    'Content-Type': req.body.contenttype
                },
                isRaw: true
            }
            break;

        case "rawresponsenocontenttype":
            context.res = {
                status: 200,
                body: req.body.value,
                isRaw: true
            }
            break;

        case "content":
            if (req.headers.return) {
                context.res = req.body;
                context.done();
            } else {
                var sendFunc = req.headers.raw ? 'raw' : 'send';
                context.res.type(req.headers.type)[sendFunc](req.body);
            }
            break;

        case "resbinding":
            context.bindings.res = { status: 202, body: "test" };
            break;

        default:
            context.res = {
                status: 400
            };
            break;
    }

    context.done();
}