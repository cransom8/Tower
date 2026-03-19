# Multiplayer Party, Queue, and Lobby Implementation Plan

## Goal

Restore working multiplayer for casual queue and join-by-code flows, then add the missing party panel, friends/online visibility, and queue population indicators so players can group up and enter matches together.

## Current State Summary

- The server already has socket handlers for parties, party invites, public queueing, private lobby creation, join-by-code, lobby invites, and friends.
- The Unity client currently exposes queue and private lobby flows, but it does not yet expose the party/friends/social system in the lobby UI.
- The repo currently contains two queue services:
  - `server/services/matchQueueManager.js` is the active service used by server startup and socket matchmaking.
  - `server/services/matchmaker.js` is still referenced by `/queue` and admin queue views.
- That split queue ownership is a likely contributor to broken or misleading queue behavior and must be cleaned up first.

## Main Problems To Solve

1. Casual queue does not reliably match players.
2. Join lobby by code is not dependable enough to support multiplayer.
3. There is no Unity party panel for grouped play.
4. There is no Unity friends/online panel for inviting party members.
5. Players cannot see how many people are currently waiting in a queue.
6. The backend and client are not aligned on a single multiplayer social flow.

## Implementation Principles

- Use one authoritative queue service only.
- Fix multiplayer reliability before adding UI polish.
- Keep party state server-authoritative.
- Keep queue entry leader-owned, but mirrored to all party members.
- Reuse the old web client social UX as reference where it helps, but rebuild it cleanly in Unity.
- Add tests for all queue, lobby, and party-critical flows before rollout.

## Phase 1: Stabilize The Core Matchmaking System

### Objectives

- Make public queue behavior reliable.
- Remove queue-service inconsistency.
- Ensure queue state, admin state, and player-visible state all come from the same source.

### Work

- Standardize on `server/services/matchQueueManager.js` as the only queue service.
- Update `server/routes/queue.js` to use `matchQueueManager` instead of the legacy `matchmaker`.
- Update admin queue endpoints in `server/routes/admin.js` to use `matchQueueManager`.
- Audit any other legacy references to `server/services/matchmaker.js`.
- Decide whether `server/services/matchmaker.js` should be deleted after migration or kept temporarily with a deprecation warning.
- Add structured logs for:
  - `queue:enter_v2`
  - `queue:leave`
  - queue heartbeat emission
  - match found
  - queue restore after failure or disconnect
  - party state transitions
- Verify that bucket keys, queue size counts, and party size calculations are consistent across:
  - socket queue entry
  - queue status heartbeat
  - admin queue dashboard
  - any HTTP queue status endpoints

### Success Criteria

- Two solo players entering casual `2v2` can be matched.
- A duo party can enter casual queue and match correctly.
- Queue size shown to clients matches server/admin counts.
- No route or UI reads stale queue state from the old queue service.

## Phase 2: Fix Join-By-Code Private Lobby Flow

### Objectives

- Make private multiplayer a dependable fallback even if public queue has issues.
- Ensure join-by-code works end to end.

### Work

- Validate and harden:
  - `lobby:create`
  - `lobby:join`
  - `lobby:leave`
  - `lobby:launch`
- Verify full happy path:
  - player A creates lobby
  - player B joins with code
  - both receive lobby update
  - host launches
  - both receive `match_found`
  - both transition into match
- Ensure stale lobby cleanup works when:
  - host leaves
  - host disconnects
  - member disconnects
  - lobby is already starting
- Improve server error messaging for:
  - invalid code
  - lobby full
  - lobby already started
  - leave queue before joining
  - already in match
- Improve Unity status/error presentation so these cases are surfaced clearly to players.

### Success Criteria

- A player can reliably join a private lobby using a code.
- Both players see the same member list.
- Launch succeeds without silent failure.

## Phase 3: Add A Shared Unity Social/Party Networking Layer

### Objectives

- Teach the Unity client about parties, friends, online state, and invites.
- Stop keeping social state only on the server side.

### Work

- Extend `unity-client/Assets/Scripts/Net/GameState.cs` with payloads for:
  - `PartySnapshot`
  - `PartyMember`
  - `FriendSummary`
  - `PartyInvitePayload`
  - `LobbyInvitePayload`
  - any small status wrappers needed for invite sent or friend events
- Extend `unity-client/Assets/Scripts/Net/NetworkManager.cs` with events and handlers for:
  - `party_update`
  - `friends_list`
  - `party_invite`
  - `party_invite_sent`
  - `lobby_invite`
  - `lobby_invite_sent`
  - `friend_request`
  - `friend_accepted`
  - `friend_removed`
  - `friend_online`
  - `friend_offline`
- Extend `unity-client/Assets/Scripts/Game/ActionSender.cs` with wrappers for:
  - `party:create`
  - `party:join`
  - `party:leave`
  - `party:invite`
  - `friend:list`
  - `friend:add`
  - `friend:accept`
  - `friend:decline`
  - `friend:remove`
  - `lobby:invite`
- Add a Unity-side controller for social state, for example:
  - `PartySocialController`
  - or a `LobbySocialController`
- That controller should own:
  - current party snapshot
  - friends list
  - online statuses
  - pending party invite
  - pending lobby invite
  - queue summary values

### Success Criteria

- Unity receives and stores live party/friend/invite state.
- UI does not need to parse raw socket payloads directly.

## Phase 4: Build The Party Panel In The Unity Lobby

### Objectives

- Let players create a party, join a party, leave a party, and queue as a group.

### Work

- Add a `PartyPanel` to the Lobby scene.
- Include:
  - party title
  - party code
  - member list
  - leader badge
  - leave party button
  - create party button
  - join party by code input
  - queue status text
  - queue/cancel buttons for leader
- Show member states such as:
  - leader
  - queued
  - in match
  - waiting for leader
- Ensure non-leaders cannot start or cancel public queue.
- Mirror party updates to all members in real time.
- Use the old removed web client UI as behavior reference, not as a direct port.

### Success Criteria

- Authenticated players can create and join parties in Unity.
- Party members can sit in a shared pre-match panel.
- One leader can queue the whole party into a match.

## Phase 5: Build The Friends And Online Panel

### Objectives

- Let players see who is online and invite them into a party or lobby.

### Work

- Add a friends panel beside or below the party panel in the Lobby scene.
- Include:
  - accepted friends list
  - online/offline indicators
  - online count summary
  - add friend input
  - invite button when eligible
  - accept/decline actions for pending requests
  - remove friend action
- Add incoming invite banners for:
  - party invite
  - lobby invite
- For v1, scope “who else is online” to accepted friends, since the server already supports that cleanly.
- If broader global online discovery is desired later, treat that as a separate feature with privacy implications.

### Success Criteria

- Players can see which friends are online.
- Leaders can invite online friends into a party.
- Players can accept an invite and join the party or lobby.

## Phase 6: Add Queue Population Indicators

### Objectives

- Show how many players are waiting in each queue before and during queueing.

### Work

- Keep the current in-queue count on the wait screen.
- Add pre-queue queue population display to the lobby buttons or queue cards.
- Add a lightweight queue summary endpoint or socket event, for example:
  - `GET /queue/summary`
  - or `queue_summary` socket broadcast
- Show values like:
  - `Casual 2v2: 6 in queue`
  - `Ranked 2v2: 2 in queue`
- Refresh queue counts every few seconds or on queue state changes.
- Ensure counts come from `matchQueueManager.getQueueStats`.

### Success Criteria

- Players can see how populated a queue is before joining.
- In-queue count and pre-queue count stay consistent with server metrics.

## Phase 7: Make Party Queueing Reliable End To End

### Objectives

- Ensure a party can enter queue, stay synchronized, match, and transition into game together.

### Work

- Verify that when a party enters queue:
  - leader sends the request
  - server validates party
  - all members receive `party_update`
  - all members receive `queue_status`
- Verify that when a party matches:
  - all members receive `match_found`
  - lane assignments are correct
  - room join/setup is consistent
  - no member is left in stale queued state
- Verify edge cases:
  - leader leaves while party is idle
  - leader disconnects while queued
  - member leaves while queued
  - host transfers correctly
  - queue is cancelled just before match found

### Success Criteria

- Queueing as a party works as reliably as solo queueing.
- No “stuck in queue forever” or split-party transitions remain.

## Phase 8: Testing And Rollout

### Automated Tests

- Add server tests for:
  - solo casual queue
  - duo casual queue
  - four solos assembling into `2v2`
  - private lobby create/join/launch
  - party create/join/leave
  - party invite rules
  - leader transfer after disconnect
  - queue restore on recoverable interruption

### Manual Multiplayer QA

- Test with at least 2 real clients.
- Recommended test matrix:
  - solo casual queue
  - duo party queue
  - create private lobby and join by code
  - invite friend to party
  - accept party invite
  - invite friend to private lobby
  - leader leaves party
  - host disconnects from lobby
  - member disconnects while queued
  - cancel queue and requeue
  - match found during reconnect window

### Rollout Order

1. Queue system cleanup and reliability fixes
2. Join-by-code private lobby fixes
3. Unity social payload/event plumbing
4. Unity party panel
5. Unity friends/online panel
6. Queue population badges and polish

## Recommended File Areas

### Server

- `server/index.js`
- `server/socket/registerHandlers.js`
- `server/services/matchQueueManager.js`
- `server/routes/queue.js`
- `server/routes/admin.js`
- `server/state/runtimeState.js`
- new or expanded server tests under `server/services/` or `server/socket/`

### Unity Client

- `unity-client/Assets/Scripts/Net/GameState.cs`
- `unity-client/Assets/Scripts/Net/NetworkManager.cs`
- `unity-client/Assets/Scripts/Game/ActionSender.cs`
- `unity-client/Assets/Scripts/UI/LobbyUI.cs`
- new social/party UI/controller scripts under `unity-client/Assets/Scripts/UI/`
- Lobby scene/prefab updates in `unity-client/Assets/Scenes/Lobby.unity`

## Risks And Notes

- The biggest near-term risk is fixing UI before fixing queue authority. That would make the new party panel look functional while multiplayer still fails underneath.
- There is a likely auth-rule mismatch to resolve. Existing comments suggest casual/private may allow guests, but the current socket handlers require auth for party and queue entry. This should be clarified before implementation.
- “See who else is online” should be carefully scoped. Friends-only online visibility is straightforward; global online visibility has product and privacy implications.
- Private lobby flow should remain the emergency multiplayer fallback even while public queue is being improved.

## Recommended First Deliverable

The first deliverable should be a backend-only stabilization pass that:

- unifies all queue reads and writes onto `matchQueueManager`
- verifies casual queue actually produces matches
- verifies join-by-code works end to end
- adds tests for the core multiplayer paths

Once that is stable, the party panel and friends panel should be built on top of it.
