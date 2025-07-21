var util = require('util');

module.exports = function (context, req) {
    context.log('Node.js HttpTrigger function invoked.');

    switch (req.headers.scenario) {
        case 'sendStatus':
            context.res.sendStatus(400);
            break;

        default:
            context.res.status(200)
                .set('test-req-header', context.req.get('test-header'))
                .set('test-header', 'Test Response Header')
                .type('application/json; charset=utf-8')
                .send({
                    reqBodyType: typeof req.body,
                    reqBodyIsArray: util.isArray(req.body),
                    reqBody: req.body,
                    reqRawBodyType: typeof req.rawBody,
                    reqRawBody: req.rawBody,
                    reqHeaders: req.headers,
                    bindingData: context.bindingData
                });
            break;
    }
};