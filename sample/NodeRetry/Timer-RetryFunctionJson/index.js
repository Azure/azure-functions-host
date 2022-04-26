var errorString = 'An error occurred';
var maxRetries = 4;

module.exports = async function (context, timer) {
    var retryContext = context.executionContext.retryContext;

    if (retryContext.maxRetryCount != maxRetries || (retryContext.retryCount > 0 && !retryContext.exception.message.includes(errorString))) {
        console.log('Unexpected error');
        throw 'Unexpected error';
    } else {
        context.log('JavaScript HTTP trigger function processed a request. retryCount: ' + retryContext.retryCount);

        if (retryContext.retryCount < maxRetries) {
            console.log(errorString);
            throw errorString;
        }
        console.log('Execution completed');
    }
}