var fs = require('fs'),
    util = require('util');

module.exports = function (context) {
    fs.writeFile('test.txt', util.inspect(context.input), function (err) {
        if (err) {
            throw err;
        }
        context.done();
    });
}