#! /usr/bin/env node

var path = require('path');
var fs = require('fs');
var spawn = require('child_process').spawn;
var fork = require('child_process').fork;
var chalk = require('chalk');
var args = process.argv;

function main() {
    var isWin = /^win/.test(process.platform);
    if (!isWin) {
        console.log('Currently all the features are only supported in Windows.\n');
        console.log('"azurefunctions new" is the only feature working across platforms.\n');
        console.log('Follow https://github.com/Azure/azure-webjobs-sdk-script/issues/509 for updates.');
        process.exit(1);
    }
    var bin = path.join(path.dirname(fs.realpathSync(__filename)), '../bin');
    var funcProc = spawn(bin + '/func.exe', args.slice(2), { stdio : [process.stdin, process.stdout, process.stderr, 'pipe']});

    funcProc.on('exit', function(code) {
        process.exit(code);
    });
}

main();