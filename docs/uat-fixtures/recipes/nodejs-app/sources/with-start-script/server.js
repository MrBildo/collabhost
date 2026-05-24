// UAT nodejs-app fixture - stdlib only, no runtime dep on `express` despite the
// package.json declaration (express is listed for probe-panel coverage only).
// PORT is read as a bare integer per the runbook §3.1 discriminator.
'use strict';

const http = require('http');

const portRaw = process.env.PORT;
const port = Number.parseInt(portRaw, 10);
if (!Number.isInteger(port) || port <= 0) {
  console.error(`[uat-fixture] invalid PORT: ${portRaw}`);
  process.exit(2);
}

const server = http.createServer((req, res) => {
  if (req.url === '/health') {
    res.writeHead(200, { 'Content-Type': 'text/plain' });
    res.end('ok');
    return;
  }
  res.writeHead(200, { 'Content-Type': 'text/plain' });
  res.end(`UAT nodejs-app fixture - PORT=${port}`);
});

server.listen(port, () => {
  console.log(`[uat-fixture] listening on PORT=${port}`);
});

const shutdown = (signal) => {
  console.log(`[uat-fixture] received ${signal}, closing`);
  server.close(() => process.exit(0));
  setTimeout(() => process.exit(0), 3000).unref();
};
process.on('SIGTERM', () => shutdown('SIGTERM'));
process.on('SIGINT', () => shutdown('SIGINT'));
