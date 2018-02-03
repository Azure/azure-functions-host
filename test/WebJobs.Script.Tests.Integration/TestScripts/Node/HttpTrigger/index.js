var util = require('util');

module.exports = function (context, req) {
    context.log('Node.js HttpTrigger function invoked.');

    context.res = {
        status: 200,
        body: {
            reqBodyType: typeof req.body,
            reqBodyIsArray: util.isArray(req.body),
            reqBody: req.body,
            reqRawBodyType: typeof req.rawBody,
            reqRawBody: req.rawBody,
            reqHeaders: req.headers,
            bindingData: context.bindingData,
            reqOriginalUrl: req.originalUrl
        },
        headers: {
            'test-header': 'Test Response Header',
            "Content-Type": "application/json; charset=utf-8"
        }
    };

    context.done();
};