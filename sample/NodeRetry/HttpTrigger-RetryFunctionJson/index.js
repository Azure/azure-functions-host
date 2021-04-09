var errorString = 'An error occurred';
var maxRetries = 4;

module.exports = async function (context, req) {
    var retryContext = context.executionContext.retryContext;

    if (retryContext.maxRetryCount != maxRetries || (retryContext.retryCount > 0 && !retryContext.exception.message.includes(errorString))) {
        context.res = {
            status: 500
        };
    } else {
        context.log('JavaScript HTTP trigger function processed a request. retryCount: ' + retryContext.retryCount);

        if (retryContext.retryCount < maxRetries) {
            throw new Error(errorString);
        }
        context.res = {
            body: 'retryCount: ' + retryContext.retryCount
        };
    }
}