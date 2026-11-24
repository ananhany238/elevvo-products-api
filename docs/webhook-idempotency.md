# How the webhook receiver stays idempotent

**Goal:** a webhook can be delivered more than once (the sender retries after a timeout, a network
glitch repeats a request, two deliveries arrive at the same moment). The effect on the database must
happen **exactly once**.

## The strategy in one sentence

The database decides. Every event id is inserted into a table with a **UNIQUE index**, in the
**same transaction** as the side effect, so whichever delivery inserts the id first wins, and every
other delivery of that id is recognised as a duplicate and does nothing.

## Step by step (one database transaction)

1. **Authenticate first.** The HMAC-SHA256 signature is checked over `timestamp + "." + raw body bytes`,
   in constant time, and the timestamp must be within 5 minutes. Nothing in the body is trusted before this.
2. **Validate the schema** (id format, supported type, positive product id, non-zero bounded delta).
3. **Begin a transaction.**
4. **Claim the event:** `INSERT` into `WebhookEvents (EventId, ...)`.
   - Unique index violation: another delivery already holds (or held) this id, so roll back and answer
     **200 duplicate**. No side effect runs.
   - A delivery that arrives *at the same time* waits on the index until the first transaction finishes,
     then fails with the same violation. This is why concurrent duplicates are safe.
5. **Apply the side effect** with one atomic statement:
   `UPDATE Products SET Stock = Stock + @delta WHERE Id = @id AND Stock + @delta >= 0`.
   It cannot lose updates from other events and cannot take stock below zero.
   If no row was changed (unknown product, or not enough stock) the transaction is **rolled back**, so
   the event id is *not* recorded and **422** is returned.
6. **Insert the outbox message** for the simulated external call, in the same transaction.
7. **Commit.** All three writes (event id, stock change, outbox message) happen together or not at all.
8. After the commit, the cached copies of the product are invalidated.

## Why it is correct

| Situation | Result |
|---|---|
| Sender retries after a timeout | Second delivery hits the unique index: 200 `duplicate`, stock changed once |
| Same event delivered 10 times concurrently | Exactly one 201, nine 200, stock changed once |
| Replayed old request | Rejected as stale (outside the 5 minute window) or, if re-signed, still a duplicate |
| Process crashes mid-way | Transaction never commits: no event id, no stock change, no outbox row, so the sender's retry processes it normally |
| Two *different* events for the same product at once | Atomic `UPDATE` serialises them: no lost update |

## The outbox (bonus)

Calling an external system *inside* the database transaction is unsafe (the call may succeed while the
transaction rolls back, or the reverse). Instead, the transaction writes an **outbox row**. A background
worker reads pending rows and performs the call:

- Success: the row is marked `ProcessedAt`.
- Failure: `Attempts` and `LastError` are updated and the **same row** is retried later with exponential
  backoff (5s, 10s, 20s ... up to 5 minutes), up to 5 attempts. A retry never creates a new event or a
  new outbox row, because both are protected by unique indexes.
- Delivery is **at least once**. If the app crashes after the external call but before the row is marked
  processed, the call repeats. The event id is sent as an **idempotency key**, so the receiving system
  can ignore the repeat.

## Known limits

- A repeated event id with a *different* body is treated as a duplicate (the first one wins). Storing a
  hash of the body would allow logging or rejecting that case.
- Run one outbox worker. To scale out, claim rows with `SELECT ... FOR UPDATE SKIP LOCKED`.
- Replays inside the 5 minute window are not blocked by the signature check alone; the idempotency
  table is what makes them harmless.
