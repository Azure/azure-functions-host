function delay(ms) {
    return new Promise(resolve => {
        setTimeout(() => {
            resolve(2);
        }, ms);
    });
}

module.exports = async function (context, req) {
    context.log('Node.js resume http trigger function processed a request');
    await delay(5000);
    let res = { status: 200 };
    context.done(null, res);
};
