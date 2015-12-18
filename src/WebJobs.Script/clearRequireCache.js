return function (context, callback) {
    Object.keys(require.cache).forEach(function (key) {
        delete require.cache[key];
    });
    callback();
}