# Room Relay API

The relay uses only Node.js core modules and stores rooms in process memory.

```sh
node server.js --host 127.0.0.1 --port 8787
```

`GET /health` returns process status and counts of active rooms, members, and objects. `POST /api/rooms/:code/join` accepts `{"displayName":"Ada"}`. Send changes to `POST .../objects/upsert` with a `clientId` and block state, or to `POST .../objects/delete` with a `clientId` and `objectId`. Clients receive changes through `GET .../events?clientId=<id>&since=<sequence>`; the request waits up to 20 seconds unless a change arrives first.

Prototype limits: 200 blocks, 32 members per room, 40 operations per second per member, 12-hour inactive-room retention, and 4,096 retained events per room. A room code grants edit access. There are no user accounts, TLS termination, durable storage, or moderation. Public hosting needs additional access controls.
