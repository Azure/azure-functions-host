var util = require('util');

module.exports = function (context, req) {
    context.log('Node.js HttpTrigger function invoked.');

    return Promise.resolve({
        // we should attempt to bind properties on the resolved object
        response: {
            status: 200,
            body: "returned from promise"
        }
    });
}