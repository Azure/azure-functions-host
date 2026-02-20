// Delay wrapper: Simulates the production scenario from ICM 51000000885979.
//
// In production, the Node.js worker took ~60 seconds to establish its gRPC connection
// (due to resource contention on a P1v3 with 27 apps). The ProcessStartupTimeout is 60s.
// The StartStream message arrived 74ms AFTER the timeout fired.
//
// For this local repro, processStartupTimeout is set to 5s in worker.config.json.
// This wrapper delays 8 seconds before starting the real worker, so StartStream
// arrives ~3s after the timeout fires — reproducing the same race condition.

const DELAY_MS = 8000; // 8 seconds > 5 second processStartupTimeout

console.error(`[delay-wrapper] Simulating slow worker startup (${DELAY_MS}ms delay)...`);
console.error(`[delay-wrapper] processStartupTimeout is 5s, so timeout will fire first.`);

setTimeout(() => {
    console.error('[delay-wrapper] Delay complete, starting real worker (too late - timeout already fired)...');
    const path = require('path');
    const realWorker = path.join(__dirname, 'nodejsWorker.real.js');
    require(realWorker);
}, DELAY_MS);
