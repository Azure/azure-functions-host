var invocationCount = 0;
var errorString = 'An error occurred';

module.exports = async function (context, req) {
    if (context.executionContext.retryContext && (context.executionContext.retryContext.retryCount !== invocationCount
        || !(context.executionContext.retryContext.maxRetryCount === 2 || context.executionContext.retryContext.maxRetryCount === 0)
        || !(context.executionContext.retryContext.exception.message.includes(errorString)))) {
        debugger;
        context.res = {
            status: 500
        };
    } else {
        const reset = req.query.reset;
        invocationCount = reset ? 0 : invocationCount

        context.log('JavaScript HTTP trigger function processed a request.invocationCount: ' + invocationCount);

        invocationCount = invocationCount + 1;
        const responseMessage = "invocationCount: " + invocationCount;
        if (invocationCount < 2) {
            throw new Error('An error occurred');
        }
        context.res = {
            // status: 200, /* Defaults to 200 */
            body: responseMessage
        };
    }
}