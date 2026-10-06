'use strict';

const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const { once } = require('node:events');
const { randomBytes } = require('node:crypto');
const net = require('node:net');
const path = require('node:path');
const { after, before, test } = require('node:test');

let processHandle;
let baseUrl;

async function reservePort() {
  const probe = net.createServer();
  probe.listen(0, '127.0.0.1');
  await once(probe, 'listening');
  const { port } = probe.address();
  await new Promise((resolve, reject) => probe.close((error) => error ? reject(error) : resolve()));
  return port;
}

function roomCode() {
  return randomBytes(5).toString('hex').toUpperCase();
}

async function waitForServer() {
  for (let attempt = 0; attempt < 50; attempt += 1) {
    if (processHandle.exitCode !== null) throw new Error(`Relay exited with code ${processHandle.exitCode}.`);
    try {
      const response = await fetch(`${baseUrl}/health`);
      if (response.ok) return;
    } catch {
      // The child process may still be starting.
    }
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  throw new Error('Relay did not become ready within five seconds.');
}

async function joinRoom(code) {
  const response = await fetch(`${baseUrl}/api/rooms/${code}/join`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ displayName: 'Test participant' }),
  });
  return { response, body: await response.json() };
}

before(async () => {
  const port = await reservePort();
  baseUrl = `http://127.0.0.1:${port}`;
  processHandle = spawn(process.execPath, [
    path.join(__dirname, '..', 'server.js'), '--host', '127.0.0.1', '--port', String(port),
  ], { stdio: 'ignore' });
  await waitForServer();
});

after(async () => {
  if (!processHandle || processHandle.exitCode !== null) return;
  processHandle.kill('SIGTERM');
  await Promise.race([
    once(processHandle, 'exit'),
    new Promise((resolve) => setTimeout(resolve, 1500)),
  ]);
});

test('health endpoint responds and invalid join names are rejected', async () => {
  const health = await fetch(`${baseUrl}/health`);
  assert.equal(health.status, 200);
  assert.equal((await health.json()).status, 'ok');

  const response = await fetch(`${baseUrl}/api/rooms/${roomCode()}/join`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ displayName: '   ' }),
  });
  assert.equal(response.status, 400);
});

test('room membership, payload validation, and event delivery work', async () => {
  const code = roomCode();
  const { response: joinResponse, body: joined } = await joinRoom(code);
  assert.equal(joinResponse.status, 200);
  assert.match(joined.clientId, /^[a-f0-9]{32}$/);

  const endpoint = `${baseUrl}/api/rooms/${code}/objects/upsert`;
  const unauthorized = await fetch(endpoint, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ clientId: 'unknown', object: {} }),
  });
  assert.equal(unauthorized.status, 401);

  const invalidObject = await fetch(endpoint, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      clientId: joined.clientId,
      object: {
        id: 'block-1',
        position: { x: 0, y: 100, z: 0 },
        rotation: { x: 0, y: 0, z: 0, w: 1 },
        colorIndex: 0,
        physicsEnabled: false,
      },
    }),
  });
  assert.equal(invalidObject.status, 400);

  const accepted = await fetch(endpoint, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      clientId: joined.clientId,
      object: {
        id: 'block-1',
        position: { x: 0, y: 1, z: 0 },
        rotation: { x: 0, y: 0, z: 0, w: 1 },
        colorIndex: 2,
        physicsEnabled: false,
      },
    }),
  });
  assert.equal(accepted.status, 200);
  assert.equal((await accepted.json()).sequence, 1);

  const events = await fetch(`${baseUrl}/api/rooms/${code}/events?clientId=${joined.clientId}&since=0`);
  assert.equal(events.status, 200);
  const batch = await events.json();
  assert.equal(batch.events.length, 1);
  assert.equal(batch.events[0].kind, 'upsert');
});

test('event cursor and oversized JSON input are rejected', async () => {
  const code = roomCode();
  const { body: joined } = await joinRoom(code);
  const cursor = await fetch(`${baseUrl}/api/rooms/${code}/events?clientId=${joined.clientId}&since=1`);
  assert.equal(cursor.status, 400);

  const oversized = await fetch(`${baseUrl}/api/rooms/${roomCode()}/join`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ displayName: 'x'.repeat(20_000) }),
  });
  assert.equal(oversized.status, 400);
});

test('a room member can hold only one pending long poll', async () => {
  const code = roomCode();
  const { body: joined } = await joinRoom(code);
  const pollUrl = `${baseUrl}/api/rooms/${code}/events?clientId=${joined.clientId}&since=0`;
  const controller = new AbortController();
  const firstPoll = fetch(pollUrl, { signal: controller.signal }).catch(() => null);
  await new Promise((resolve) => setTimeout(resolve, 100));

  const duplicatePoll = await fetch(pollUrl);
  assert.equal(duplicatePoll.status, 429);
  controller.abort();
  await firstPoll;

  for (let attempt = 0; attempt < 20; attempt += 1) {
    const health = await (await fetch(`${baseUrl}/health`)).json();
    if (health.pendingPolls === 0) return;
    await new Promise((resolve) => setTimeout(resolve, 25));
  }
  assert.fail('Aborted long poll remained registered.');
});
