# How identity and room access are enforced (real-time notifications)

**Goal:** a user receives their own live status updates, and can never subscribe to, or receive,
another user's updates, even with a modified client.

The app uses ASP.NET Core **SignalR** (the .NET equivalent of Socket.IO: the same concepts of
connections, events and groups, which are SignalR's "rooms").

## 1. Identity: a server-verified token

- `POST /api/auth/login` checks the username and password against the two local test users
  (constant-time comparison; unknown user and wrong password give the same 401).
- It returns a short-lived **JWT** signed with a secret that only the server knows (HMAC-SHA256, the
  algorithm is pinned). The user id is a claim **inside** the signed token.
- A client cannot change the user id without breaking the signature, and cannot sign a token itself.
- The hub is marked `[Authorize]`. The JWT middleware checks signature, issuer, audience and
  expiry before the hub code runs. No valid token, no connection.
- WebSockets cannot send an `Authorization` header from a browser, so the token is also accepted as
  `?access_token=` **on the hub path only**.

## 2. Rooms: derived from the verified identity, never chosen by the client

- In `OnConnectedAsync`, the server reads the user id from the **verified token** and adds the
  connection to the group `user:{userId}`.
- **There is no hub method for joining or leaving a room.** The only method a client can call is
  `Acknowledge(eventId)`. A user id placed in the URL, a header or a message is never used.
- Events are published to `Clients.Group("user:{userId}")`, so they can only reach connections that
  the server itself put in that room.

## 3. Disconnects and reconnections

- Group membership belongs to **one connection**. When a connection drops, the server forgets it.
- A reconnect is a **new connection**: it must present a token again, and the room is derived again
  from that token. Nothing from the old connection is trusted or reused.
- The client logs in again for every connection attempt (including automatic reconnects), so it
  always presents a fresh token.
- `CloseOnAuthenticationExpiration` closes a connection when its token expires, so a long-lived
  connection cannot outlive its authorization.

## 4. Acknowledgements, retries and duplicates (bonus)

- Every event has a unique **event id**, a status and a timestamp.
- The server keeps each sent event as "pending" until the client calls `Acknowledge`. Pending events
  are keyed by the **verified user id and the event id**, so a user can only acknowledge their own
  events (acknowledging someone else's id changes nothing).
- Unacknowledged events are re-sent with growing delays (2s, 4s, 8s ... capped at 30s) up to a
  maximum number of attempts (5 by default), then dropped with a log line. Retries are **bounded**.
- When a user reconnects, their still-pending events are sent again to that user only.
- Because an event can legitimately arrive more than once, the client keeps the ids it has already
  shown and ignores repeats.

## 5. The simulation endpoint

`POST /api/dev/simulate-status` stands in for the order system. It is protected by a server-side
key (`X-Api-Key`) that is not a user token, and it is **disabled (404)** when no key is configured.
A user's JWT cannot be used to push events.

## Threat table

| Attack | Why it fails |
|---|---|
| Connect without a token, or with garbage | `[Authorize]`: 401 |
| Forge a token for another user | The signature needs the server key |
| Reuse an expired token | Lifetime is validated; open connections close at expiry |
| Put `?userId=bob` in the URL | The user id is only read from the verified token |
| Call `JoinGroup("user:bob")` | The method does not exist on the hub |
| Acknowledge another user's event | Pending events are keyed by the verified user id |
| Reconnect and keep the old room | Rooms belong to a connection, and are derived again from the new token |
| Call the simulation endpoint | Needs the server key (404 if disabled) |

## Known limits

- Pending events and groups live in memory: this is a single-instance design. To scale out, use a
  SignalR backplane (for example Redis) and store pending events in Redis or a database.
- Events for users who are offline are only kept until the retry limit; there is no long-term inbox.
- Tokens sent in the query string can appear in proxy logs, so tokens must be short-lived.
- The demo users and secrets come from the environment; a real system would use a user database
  with hashed passwords (and login throttling).
