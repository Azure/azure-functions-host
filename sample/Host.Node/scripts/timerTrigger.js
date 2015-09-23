var fs = require('fs');

module.exports = function (context) {
    var timeStamp = new Date().toISOString();
    context.log('Node.js timer trigger function ran at ' + timeStamp);

    fs.appendFile('joblog.txt', timeStamp + '\r\n', function (err) {
        if (err) {
            throw err;
        }
        context.done();
    });
}