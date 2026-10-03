'use strict';

const http = require('node:http');
const { randomUUID } = require('node:crypto');
const { URL } = require('node:url');

const PORT = readIntegerArg('--port', process.env.PORT || '8787', 1, 65535);
const HOST = readStringArg('--host', process.env.HOST || '127.0.0.1');
const BODY_LIMIT = 16 * 1024;
const MAX_OBJECTS = 200;
const MAX_ROOMS = 500;
const MAX_MEMBERS_PER_ROOM = 32;
const MAX_EVENTS = 4096;
const MAX_OPS_PER_SECOND = 40;
const ROOM_TTL_MS = 12 * 60 * 60 * 1000;
const LONG_POLL_MS = 20_000;
const rooms = new Map();

function readStringArg(name, fallback) {
  const index = process.argv.indexOf(name);
  return index >= 0 && process.argv[index + 1] ? process.argv[index + 1] : fallback;
}

function readIntegerArg(name, fallback, minimum, maximum) {
  const value = Number(readStringArg(name, fallback));
  if (!Number.isInteger(value) || value < minimum || value > maximum) {
    throw new Error(`Invalid ${name}: expected an integer from ${minimum} to ${maximum}.`);
  }
  return value;
}

function getRoom(code, create = false) {
  let room = rooms.get(code);
  if (!room && create) {
    room = {
      code,
      sequence: 0,
      lastActivity: Date.now(),
      members: new Map(),
      objects: new Map(),
      events: [],
      waiters: new Set(),
    };
    rooms.set(code, room);
  }
  if (room) room.lastActivity = Date.now();
  return room;
}

function json(res, status, value) {
  const body = JSON.stringify(value);
  res.writeHead(status, {
    'content-type': 'application/json; charset=utf-8',
    'content-length': Buffer.byteLength(body),
    'cache-control': 'no-store',
    'x-content-type-options': 'nosniff',
  });
  res.end(body);
}

function badRequest(res, message) {
  json(res, 400, { error: message });
}

async function readJson(req) {
  const contentLength = Number(req.headers['content-length'] || 0);
  if (contentLength > BODY_LIMIT) throw new Error('Request body is too large.');

  let size = 0;
  const chunks = [];
  for await (const chunk of req) {
    size += chunk.length;
    if (size > BODY_LIMIT) throw new Error('Request body is too large.');
    chunks.push(chunk);
  }
  try {
    return JSON.parse(Buffer.concat(chunks).toString('utf8'));
  } catch {
    throw new Error('Request body must be valid JSON.');
  }
}

function finiteNumber(value, label, min, max) {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < min || value > max) {
    throw new Error(`${label} is outside the allowed range.`);
  }
  return value;
}

function normalizeObject(value) {
  if (!value || typeof value !== 'object') throw new Error('Object payload is missing.');
  if (typeof value.id !== 'string' || !/^[A-Za-z0-9_-]{1,64}$/.test(value.id)) {
    throw new Error('Object id is invalid.');
  }
  const position = value.position;
  const rotation = value.rotation;
  if (!position || !rotation) throw new Error('Object transform is missing.');
  const normalizedPosition = {
    x: finiteNumber(position.x, 'position.x', -50, 50),
    y: finiteNumber(position.y, 'position.y', -10, 20),
    z: finiteNumber(position.z, 'position.z', -50, 50),
  };
  const q = {
    x: finiteNumber(rotation.x, 'rotation.x', -1.01, 1.01),
    y: finiteNumber(rotation.y, 'rotation.y', -1.01, 1.01),
    z: finiteNumber(rotation.z, 'rotation.z', -1.01, 1.01),
    w: finiteNumber(rotation.w, 'rotation.w', -1.01, 1.01),
  };
  const magnitude = Math.hypot(q.x, q.y, q.z, q.w);
  if (magnitude < 0.5 || magnitude > 1.5) throw new Error('Object rotation is invalid.');
  if (!Number.isInteger(value.colorIndex) || value.colorIndex < 0 || value.colorIndex > 5) {
    throw new Error('Object color is invalid.');
  }
  if (typeof value.physicsEnabled !== 'boolean') throw new Error('Physics state is invalid.');

  return {
    id: value.id,
    position: normalizedPosition,
    rotation: {
      x: q.x / magnitude,
      y: q.y / magnitude,
      z: q.z / magnitude,
      w: q.w / magnitude,
    },
    colorIndex: value.colorIndex,
    physicsEnabled: value.physicsEnabled,
  };
}

function getMember(room, clientId) {
  return typeof clientId === 'string' ? room.members.get(clientId) : undefined;
}

function consumeOperation(member) {
  const now = Date.now();
  if (now - member.windowStart >= 1000) {
    member.windowStart = now;
    member.operationCount = 0;
  }
  member.operationCount += 1;
  return member.operationCount <= MAX_OPS_PER_SECOND;
}

function makeEvent(room, event) {
  const entry = { sequence: ++room.sequence, ...event };
  room.events.push(entry);
  if (room.events.length > MAX_EVENTS) room.events.splice(0, room.events.length - MAX_EVENTS);
  room.lastActivity = Date.now();
  wakeWaiters(room);
  return entry;
}

function currentEvents(room, since) {
  const oldest = room.events.length ? room.events[0].sequence : room.sequence + 1;
  if (since < oldest - 1) {
    return {
      sequence: room.sequence,
      resyncRequired: true,
      objects: Array.from(room.objects.values()),
      events: [],
    };
  }
  const events = room.events.filter((event) => event.sequence > since).slice(0, 256);
  return {
    sequence: events.length ? events[events.length - 1].sequence : room.sequence,
    resyncRequired: false,
    objects: [],
    events,
  };
}

function wakeWaiters(room) {
  for (const waiter of Array.from(room.waiters)) {
    const batch = currentEvents(room, waiter.since);
    if (batch.resyncRequired || batch.events.length) finishWaiter(room, waiter, batch);
  }
}

function finishWaiter(room, waiter, batch) {
  if (!room.waiters.delete(waiter)) return;
  clearTimeout(waiter.timeout);
  waiter.res.removeListener('close', waiter.onClose);
  if (!waiter.res.writableEnded) json(waiter.res, 200, batch);
}

function waitForEvents(room, res, since) {
  const immediate = currentEvents(room, since);
  if (immediate.resyncRequired || immediate.events.length) {
    json(res, 200, immediate);
    return;
  }

  const waiter = { res, since, timeout: null, onClose: null };
  waiter.onClose = () => {
    clearTimeout(waiter.timeout);
    room.waiters.delete(waiter);
  };
  waiter.timeout = setTimeout(() => {
    finishWaiter(room, waiter, { sequence: room.sequence, resyncRequired: false, objects: [], events: [] });
  }, LONG_POLL_MS);
  res.once('close', waiter.onClose);
  room.waiters.add(waiter);
}

function routeRoom(pathname) {
  const match = /^\/api\/rooms\/([A-Z0-9]{6,12})(?:\/(.*))?$/.exec(pathname);
  return match ? { code: match[1], action: match[2] || '' } : null;
}

async function handle(req, res) {
  const url = new URL(req.url, 'http://localhost');
  if (req.method === 'GET' && url.pathname === '/health') {
    json(res, 200, {
      status: 'ok',
      rooms: rooms.size,
      members: Array.from(rooms.values()).reduce((total, room) => total + room.members.size, 0),
      objects: Array.from(rooms.values()).reduce((total, room) => total + room.objects.size, 0),
    });
    return;
  }

  const route = routeRoom(url.pathname);
  if (!route) {
    json(res, 404, { error: 'Route not found.' });
    return;
  }

  if (req.method === 'POST' && route.action === 'join') {
    let input;
    try {
      input = await readJson(req);
    } catch (error) {
      badRequest(res, error.message);
      return;
    }
    if (!input || typeof input !== 'object' || Array.isArray(input)) {
      badRequest(res, 'Join payload must be a JSON object.');
      return;
    }
    const displayName = typeof input.displayName === 'string' ? input.displayName.trim() : '';
    if (displayName.length < 1 || displayName.length > 24) {
      badRequest(res, 'Display name must contain 1 to 24 characters.');
      return;
    }
    if (!rooms.has(route.code) && rooms.size >= MAX_ROOMS) {
      json(res, 503, { error: 'Server room limit reached. Try again later.' });
      return;
    }
    const room = getRoom(route.code, true);
    if (room.members.size >= MAX_MEMBERS_PER_ROOM) {
      json(res, 409, { error: `Room is limited to ${MAX_MEMBERS_PER_ROOM} members.` });
      return;
    }
    const clientId = randomUUID().replaceAll('-', '');
    room.members.set(clientId, { displayName, windowStart: Date.now(), operationCount: 0 });
    json(res, 200, {
      roomCode: room.code,
      clientId,
      sequence: room.sequence,
      objects: Array.from(room.objects.values()),
    });
    return;
  }

  const room = getRoom(route.code);
  if (!room) {
    json(res, 404, { error: 'Room not found. Join it before requesting state.' });
    return;
  }

  if (req.method === 'GET' && route.action === 'events') {
    const since = Number(url.searchParams.get('since') || '0');
    const clientId = url.searchParams.get('clientId') || '';
    if (!getMember(room, clientId)) {
      json(res, 401, { error: 'Join the room before polling events.' });
      return;
    }
    if (!Number.isSafeInteger(since) || since < 0 || since > room.sequence) {
      badRequest(res, 'Event cursor is invalid.');
      return;
    }
    waitForEvents(room, res, since);
    return;
  }

  if (req.method === 'POST' && (route.action === 'objects/upsert' || route.action === 'objects/delete')) {
    let input;
    try {
      input = await readJson(req);
    } catch (error) {
      badRequest(res, error.message);
      return;
    }
    if (!input || typeof input !== 'object' || Array.isArray(input)) {
      badRequest(res, 'Object operation payload must be a JSON object.');
      return;
    }
    const member = getMember(room, input.clientId);
    if (!member) {
      json(res, 401, { error: 'Join the room before editing objects.' });
      return;
    }
    if (!consumeOperation(member)) {
      json(res, 429, { error: 'Too many updates. Reduce the physics sync rate.' });
      return;
    }

    if (route.action === 'objects/upsert') {
      let objectState;
      try {
        objectState = normalizeObject(input.object);
      } catch (error) {
        badRequest(res, error.message);
        return;
      }
      if (!room.objects.has(objectState.id) && room.objects.size >= MAX_OBJECTS) {
        json(res, 409, { error: `Room is limited to ${MAX_OBJECTS} objects.` });
        return;
      }
      room.objects.set(objectState.id, objectState);
      const event = makeEvent(room, { kind: 'upsert', clientId: input.clientId, object: objectState });
      json(res, 200, { sequence: event.sequence });
      return;
    }

    if (typeof input.objectId !== 'string' || !/^[A-Za-z0-9_-]{1,64}$/.test(input.objectId)) {
      badRequest(res, 'Object id is invalid.');
      return;
    }
    room.objects.delete(input.objectId);
    const event = makeEvent(room, { kind: 'delete', clientId: input.clientId, objectId: input.objectId });
    json(res, 200, { sequence: event.sequence });
    return;
  }

  json(res, 404, { error: 'Route not found.' });
}

const server = http.createServer((req, res) => {
  handle(req, res).catch((error) => {
    if (!res.headersSent) json(res, 500, { error: 'Unexpected server error.' });
    else res.destroy();
    console.error(error);
  });
});

const cleanupTimer = setInterval(() => {
  const cutoff = Date.now() - ROOM_TTL_MS;
  for (const [code, room] of rooms) {
    if (room.lastActivity < cutoff && room.waiters.size === 0) rooms.delete(code);
  }
}, 60_000);
cleanupTimer.unref();

server.listen(PORT, HOST, () => {
  console.log(`Shared AR Workshop API listening on http://${HOST}:${PORT}`);
});

function shutdown() {
  clearInterval(cleanupTimer);
  for (const room of rooms.values()) {
    for (const waiter of Array.from(room.waiters)) {
      finishWaiter(room, waiter, { sequence: room.sequence, resyncRequired: false, objects: [], events: [] });
    }
  }
  server.close(() => process.exit(0));
}

process.on('SIGINT', shutdown);
process.on('SIGTERM', shutdown);
