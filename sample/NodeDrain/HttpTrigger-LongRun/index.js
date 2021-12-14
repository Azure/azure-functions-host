var errorString = 'An error occurred';

function delay(delayInms) {
    return new Promise(resolve => {
        setTimeout(() => {
            resolve(2);
        }, delayInms);
    });
}

module.exports = async function (context, req) {
    await delay(1000);
    throw new Error(errorString);
}