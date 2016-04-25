module.exports = function (context, req) {
    context.log('Node.js HttpTrigger function invoked.');

    context.res = {
        status: 200,
        body: {
            reqBody: req.body,
            reqBodyType: typeof req.body,
            reqRawBody: req.rawBody,
            reqRawBodyType: typeof req.rawBody,
            reqHeaders: req.headers,
            bindingData: context.bindingData
        },
        headers: {
            'test-header': 'Test Response Header'
        }
    };

    context.done();
}