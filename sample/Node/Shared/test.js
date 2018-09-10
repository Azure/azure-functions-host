var timestamp = new Date().getTime();

module.exports = {
    timestamp: timestamp,
    greeting: function (name) {
        return 'Hello ' + name;
    }
};