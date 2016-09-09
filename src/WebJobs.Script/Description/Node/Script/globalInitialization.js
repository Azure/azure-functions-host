// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

var process = require('process');

return function (context, callback) {
    process.on('uncaughtException', function (err) {
        context.handleUncaughtException(err.stack);
    });

    // TEMP HACK: workaround for https://github.com/tjanczuk/edge/issues/325
    process.nextTick = global.setImmediate;

    callback();
};

