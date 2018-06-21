var util = require('util');

module.exports = function (context, req) {
    var scenario = (req.headers && req.headers.scenario) || req.body.scenario;
    var enableContentNegotiation = (req.headers.negotiation == `true`);

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
                }
            };
            break;

        case "rawresponsenocontenttype":
            context.res = {
                status: 200,
                body: req.body.value
            };
            break;

        case "content":
            context.res.enableContentNegotiation = enableContentNegotiation;
            context.res.type(req.headers.type)[`send`](req.body);
            break;

        case "resbinding":
            context.bindings.res = { status: 202, body: "test" };
            break;

        case "nullbody":
            context.res = { status: 204, body: null, headers: { 'content-type': 'application/json' } };
            break;

        case "appInsights-Success":
            logAppInsightsPayload(context, req.body.value);
            context.res = {
                status: 200,
                body: context.invocationId
            };
            break;

        case "appInsights-Failure":
            logAppInsightsPayload(context, req.body.value);
            context.res = {
                status: 409,
                body: context.invocationId
            };
            break;

        case "appInsights-Throw":
            logAppInsightsPayload(context, req.body.value);
            throw new Error(context.invocationId);

        default:
            context.res = {
                status: 400
            };
            break;
    }

    context.done();
};

function logAppInsightsPayload(context, functionTrace) {
    var logPayload = {
        invocationId: context.invocationId,
        trace: functionTrace
    };

    context.log(logPayload);
}