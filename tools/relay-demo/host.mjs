import http from 'node:http';
import net from 'node:net';
import { spawn } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const directory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(directory, '..', '..');
const relayEntry = path.join(repositoryRoot, 'server', 'server.js');
const pagePath = path.join(directory, 'index.html');

function reserveLoopbackPort() {
  return new Promise((resolve, reject) => {
    const probe = net.createServer();
    probe.once('error', reject);
    probe.listen(0, '127.0.0.1', () => {
      const address = probe.address();
      if (!address || typeof address === 'string') {
        probe.close(() => reject(new Error('Could not reserve a loopback port.')));
        return;
      }
      probe.close((error) => error ? reject(error) : resolve(address.port));
    });
  });
}

async function waitForRelay(url, child) {
  const deadline = Date.now() + 8000;
  let lastError;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) throw new Error(`Relay exited with code ${child.exitCode}.`);
    try {
      const response = await fetch(`${url}/health`, { signal: AbortSignal.timeout(500) });
      if (response.ok && (await response.json()).status === 'ok') return;
      lastError = new Error(`Relay health endpoint returned HTTP ${response.status}.`);
    } catch (error) {
      lastError = error;
    }
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  throw new Error(`Relay did not become healthy: ${lastError?.message || 'timeout'}`);
}

function sendJson(response, status, payload) {
  const body = JSON.stringify(payload);
  response.writeHead(status, {
    'content-type': 'application/json; charset=utf-8',
    'content-length': Buffer.byteLength(body),
    'cache-control': 'no-store',
    'x-content-type-options': 'nosniff',
    'x-frame-options': 'DENY',
    'referrer-policy': 'no-referrer',
  });
  response.end(body);
}

function allowedApiRequest(method, pathname) {
  if (method === 'GET' && pathname === '/health') return true;
  if (method === 'POST' && /^\/api\/rooms\/[A-Z0-9]{8,12}\/join$/.test(pathname)) return true;
  if (method === 'GET' && /^\/api\/rooms\/[A-Z0-9]{8,12}\/events$/.test(pathname)) return true;
  if (method === 'POST' && /^\/api\/rooms\/[A-Z0-9]{8,12}\/objects\/upsert$/.test(pathname)) return true;
  return false;
}

async function main() {
  const relayPort = await reserveLoopbackPort();
  const webPort = await reserveLoopbackPort();
  const relayUrl = `http://127.0.0.1:${relayPort}`;
  const child = spawn(process.execPath, [relayEntry, '--host', '127.0.0.1', '--port', String(relayPort)], {
    cwd: repositoryRoot,
    stdio: 'inherit',
    windowsHide: true,
  });
  child.once('error', (error) => console.error(`Could not start the relay: ${error.message}`));
  try {
    await waitForRelay(relayUrl, child);
  } catch (error) {
    if (child.exitCode === null) child.kill();
    throw error;
  }

  const page = await readFile(pagePath);
  const host = http.createServer((request, response) => {
    const url = new URL(request.url || '/', `http://127.0.0.1:${webPort}`);
    if (request.method === 'GET' && url.pathname === '/') {
      response.writeHead(200, {
        'content-type': 'text/html; charset=utf-8',
        'content-length': page.byteLength,
        'cache-control': 'no-store',
        'content-security-policy': "default-src 'self'; connect-src 'self'; img-src 'self' data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
        'x-content-type-options': 'nosniff',
        'x-frame-options': 'DENY',
        'referrer-policy': 'no-referrer',
      });
      response.end(page);
      return;
    }
    if (request.method === 'GET' && url.pathname === '/favicon.ico') {
      response.writeHead(204, { 'cache-control': 'no-store' });
      response.end();
      return;
    }

    if (!allowedApiRequest(request.method, url.pathname)) {
      sendJson(response, 404, { error: 'Demo route not found.' });
      return;
    }
    const expectedOrigin = `http://127.0.0.1:${webPort}`;
    if (request.headers.host !== `127.0.0.1:${webPort}` ||
        (request.headers.origin && request.headers.origin !== expectedOrigin)) {
      sendJson(response, 403, { error: 'The local demo accepts requests only from its loopback origin.' });
      return;
    }

    const headers = {};
    if (typeof request.headers.authorization === 'string') {
      headers.authorization = request.headers.authorization;
    }
    if (typeof request.headers['content-type'] === 'string') {
      headers['content-type'] = request.headers['content-type'];
    }
    const upstream = http.request({
      hostname: '127.0.0.1',
      port: relayPort,
      path: `${url.pathname}${url.search}`,
      method: request.method,
      headers,
      timeout: 25_000,
    }, (upstreamResponse) => {
      const responseHeaders = {
        'cache-control': 'no-store',
        'x-content-type-options': 'nosniff',
      };
      for (const name of ['content-type', 'retry-after']) {
        const value = upstreamResponse.headers[name];
        if (typeof value === 'string') responseHeaders[name] = value;
      }
      response.writeHead(upstreamResponse.statusCode || 502, responseHeaders);
      upstreamResponse.pipe(response);
    });
    upstream.once('timeout', () => upstream.destroy(new Error('Relay request timed out.')));
    upstream.once('error', (error) => {
      if (!response.headersSent) sendJson(response, 502, { error: `Relay request failed: ${error.message}` });
      else response.destroy(error);
    });
    request.pipe(upstream);
  });

  host.on('error', (error) => {
    console.error(`Demo host failed: ${error.message}`);
    child.kill();
    process.exitCode = 1;
  });

  host.listen(webPort, '127.0.0.1', () => {
    console.log(`Browser relay harness: http://127.0.0.1:${webPort}`);
    console.log('Relay: loopback only; all room data is held in process memory. Press Ctrl+C to stop both.');
  });

  let shuttingDown = false;
  const shutdown = () => {
    if (shuttingDown) return;
    shuttingDown = true;
    host.close(() => {});
    if (child.exitCode === null) child.kill();
    const forceExit = setTimeout(() => process.exit(1), 2500);
    forceExit.unref();
    child.once('exit', () => {
      clearTimeout(forceExit);
      process.exit(0);
    });
  };
  process.once('SIGINT', shutdown);
  process.once('SIGTERM', shutdown);
}

main().catch((error) => {
  console.error(error.message);
  process.exitCode = 1;
});
