var fs = require('fs');

module.exports = function (input, callback) {
    var timeStamp = new Date().toISOString();
    console.log('Node.js scheduled job function ran at ' + timeStamp);

    fs.appendFile('joblog.txt', timeStamp + '\r\n', function (err) {
        if (err) {
            throw err;
        }
        callback();
    });
}