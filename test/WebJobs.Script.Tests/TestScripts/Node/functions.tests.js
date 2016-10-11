var expect = require('chai').expect;

var config = process.argv[process.argv.length - 1]
config = (config.indexOf('--config=') !== -1)? config.substr(9): 'Debug';

var functions = require('../../bin/' + config + '/Content/Script/functions.js');

var context = {};
var logs = [];

describe('functions', () => {
    beforeEach(() => {
        logs = [];
        context = {
            _inputs: [],
            log: (message) => logs.push(message),
            bind: (val, cb) => cb && cb(val)
        };
    });

    it('clears require cache', (done) => {
        expect(Object.keys(require.cache).length).to.not.equal(0);

        functions.clearRequireCache(undefined, () => {
            expect(Object.keys(require.cache).length).to.equal(0);
            done();
        });
    });

    describe('entry point', () => {
        it('runs single export', () => {
            var run = false;
            var func = functions.createFunction({
                f: () => run = true,
            });

            func(context);

            expect(run).to.be.true;
        });

        it('runs named entry', () => {
            var run = false;
            var func = functions.createFunction({
                named: () => run = true,
                other: () => run = false
            });

            context._entryPoint = 'named';
            func(context);

            expect(run).to.be.true;
        });

        it('falls back to run function', () => {
            var run = false;
            var func = functions.createFunction({
                run: () => run = true,
                other: () => run = false
            });

            func(context);

            expect(run).to.be.true;
        });

        it('falls back to index function', () => {
            var run = false;
            var func = functions.createFunction({
                index: () => run = true,
                other: () => run = false
            });

            func(context);

            expect(run).to.be.true;
        });

        it('throws if no function', () => {
            var func = functions.createFunction(1);

            expect(() => func(context)).to.throw(/Unable to determine function entry point.*/);
        });
    });

    describe('wrapper', () => {
        it('logs if double done', () => {
            var func = functions.createFunction((context) => {
                context.done();
                context.done();
                expect(logs[0]).to.match(/Error: 'done' has already been called.*/);
            });

            func(context, () => {});
            
        });

        it('logs if promise and done', (done) => {
            var func = functions.createFunction((context) => {
                context.done();
                return Promise.resolve('test');
            });

            func(context, () => {
                setImmediate(() => {
                    expect(logs[0]).to.match(/Error: Choose either to return a promise or call 'done'.*/);
                    done();
                });
            });
        });

        it('done passes data to binder', () => {
            var func = functions.createFunction((context) => {
                context.bindings = { result: 'res' };
                context.done();
            });

            func(context, (results) => {
                expect(results).to.eql({ result: 'res' });
            });
        });

        it('done passes error', () => {
            var func = functions.createFunction((context) => {
                context.done('err');
            });

            func(context, (results) => {
                expect(results).to.eql('err');
            });
        });

        it('promise passes error', (done) => {
            var func = functions.createFunction((context) => {
                return Promise.reject('err');
            });

            func(context, (results) => {
                expect(results).to.eql('err');
                done();
            });
        });
    });
});