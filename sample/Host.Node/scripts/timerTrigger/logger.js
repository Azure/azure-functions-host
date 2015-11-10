var fs = require('fs');

module.exports.log = function (text, callback) {
    fs.appendFile('joblog.txt', text + '\r\n', function (err) {
        if (err) {
            throw err;
        }
        callback();
    });
}