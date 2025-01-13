const func = require('@azure/functions-core');

func.registerHook('postInvocation', async () => {
    // Add slight delay to ensure logs show up before the invocation finishes
    // See these issues for more info:
    // https://github.com/Azure/azure-functions-host/issues/9238
    // https://github.com/Azure/azure-functions-host/issues/8222
    await new Promise((resolve) => setTimeout(resolve, 100));
});