// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

var util = require('util');
var process = require('process');
var request = require('./http/request');
var response = require('./http/response');

module.exports = {
    globalInitialization: globalInitialization,
    clearRequireCache: clearRequireCache,
    createFunction: createFunction
};

function globalInitialization(context, callback) {
    process.on('uncaughtException', function (err) {
        context.handleUncaughtException(err.stack);
    });
    callback();
}

function clearRequireCache(context, callback) {
    Object.keys(require.cache).forEach(function (key) {
        delete require.cache[key];
    });
    callback();
}

function createFunction(f) {
    return function (context, callback) {
        // TEMP HACK: workaround for https://github.com/tjanczuk/edge/issues/325
        setImmediate(() => { });

        f = getEntryPoint(f, context);

        // configure loggers
        var origLog = context.log;
        var logLevel = function (traceLevel) {
            return function () {
                var message = util.format.apply(null, arguments);
                origLog({ lvl: traceLevel, msg: message });
            };
        };
        // set default log to 'info'
        var log = logLevel(3);
        ['error', 'warn', 'info', 'verbose'].forEach((level, index) => {
            var traceLevel = index + 1;
            log[level] = logLevel(traceLevel);
        });
        context.log = log;

        var origMetric = context._metric;
        delete context._metric;
        context.log.metric = function (name, value, properties) {
            origMetric({ name: name, value: value, properties: properties});
        };        

        context.done = function (err, returnValue) {
            if (context._done) {
                if (context._promise) {
                    context.log("Error: Choose either to return a promise or call 'done'.  Do not use both in your script.");
                } else {
                    context.log("Error: 'done' has already been called. Please check your script for extraneous calls to 'done'.");
                }
                return;
            }
            context._done = true;

            if (err) {
                callback(err);
            }
            else {
                if (context.res && context.bindings.res === undefined) {
                    context.bindings.res = context.res;
                }

                // because Edge.JS interop doesn't flow new values added to objects,
                // we capture the binding values and pass them back as part of the
                // result
                var bindingValues = {};
                for (var name in context.bindings) {
                    bindingValues[name] = context.bindings[name];
                }

                var result = {
                    returnValue: returnValue,
                    bindingValues: bindingValues
                };
                callback(null, result);
            }
        };

        var inputs = context._inputs;
        inputs.unshift(context);
        delete context._inputs;

        var lowercaseTrigger = context._triggerType && context._triggerType.toLowerCase();
        switch (lowercaseTrigger) {
            case "httptrigger":
                context.req = request(context);
                context.res = response(context);
                break;
        }
        delete context._triggerType;

        var result = f.apply(null, inputs);
        if (result && util.isFunction(result.then)) {
            context._promise = true;
            result.then((result) => context.done(null, result))
                .catch((err) => context.done(err));
        }
    };
}

function getEntryPoint(f, context) {
    if (util.isObject(f)) {
        if (context._entryPoint) {
            // the module exports multiple functions
            // and an explicit entry point was named
            f = f[context._entryPoint];
            delete context._entryPoint;
        }
        else if (Object.keys(f).length === 1) {
            // a single named function was exported
            var name = Object.keys(f)[0];
            f = f[name];
        }
        else {
            // finally, see if there is an exported function named
            // 'run' or 'index' by convention
            f = f.run || f.index;
        }
    }

    if (!util.isFunction(f)) {
        throw "Unable to determine function entry point. If multiple functions are exported, " +
        "you must indicate the entry point, either by naming it 'run' or 'index', or by naming it " +
        "explicitly via the 'entryPoint' metadata property.";
    }

    return f;
}