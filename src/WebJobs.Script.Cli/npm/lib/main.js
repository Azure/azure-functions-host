#! /usr/bin/env node

var path = require('path');
var fs = require('fs');
var spawn = require('child_process').spawn;
var fork = require('child_process').fork;
var chalk = require('chalk');
var args = process.argv;

function main() {
    if ((args.length == 3 && args[2] === 'new') ||
        (args.length > 3) && args[2] === 'new' && args[3] === 'function') {
        var yo = path.join(path.dirname(fs.realpathSync(__filename)), '../node_modules/yo/lib/cli.js');
        var yoProc = fork(yo, ['azurefunctions']);
        yoProc.on('exit', function (code) {
            console.log('\n');
            console.log(chalk.cyan('Tip:') + ' run ' + chalk.yellow('`func run <functionName>`') + ' to run the function.');
            process.exit(code);
        });
    } else {
        var isWin = /^win/.test(process.platform);
        if (!isWin) {
            console.log('Currently only Windows is supported.\nFollow https://github.com/Azure/azure-webjobs-sdk-script/issues/509 for updates.');
            process.exit(1);
        }
        var bin = path.join(path.dirname(fs.realpathSync(__filename)), '../bin');
        var funcProc = spawn(bin + '/func.exe', args.slice(2), { stdio : [process.stdin, process.stdout, process.stderr, 'pipe']});

        funcProc.on('exit', function(code) {
            process.exit(code);
        });
    }
}

main();