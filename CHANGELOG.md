# Changelog

Project history from the earliest recorded baseline through October 24, 2026. Entries are chronological, from creation to the current build.

This is a development changelog, not a release announcement. No public version numbers or release dates are implied. Dates come from the historical implementation log and Git checkpoints; some commits bundle several iterations. Historical behavior is described as it existed at the time, with later replacements called out explicitly.

## 2026-08-30 — Project baseline and architectural direction

### Established

- Audited the existing Unity project, Editor scene and Console state, active Windows build configuration, package dependencies, serialization settings, and recovery material.
- Retained the existing URP 2D direction, Input System, text serialization, and visible metadata.
- Resolved the game as fixed top-down 2D. “NSEW viewing” means a fixed world view, not camera rotation between cardinal viewpoints.
- Defined independent player movement and aiming, with absolute mouse position determining world-space aim rather than mouse delta.
- Established the gameplay boundary: Input → command/intention → authoritative simulation → state → presentation.
- Kept Core logic independent of Unity and networking. Input and UI request actions instead of owning authoritative gameplay state.
- Deferred networking packages until the separately approved early co-op technical proof.

### Application foundation

- Added Boot, Main Menu, and Gameplay composition with explicit scene/prefab dependencies.
- Added application commands and validated state transitions, asynchronous scene loading, loading-time duplicate-request protection, and failure handling.
- Added UI Toolkit screens and New Game, Return to Main Menu, and Exit actions.
- Kept unimplemented Continue, manual Load Game, and Options actions unavailable rather than presenting nonfunctional features as complete.
- Added assembly boundaries, validation utilities, initial Edit/Play Mode tests, and a Windows Development build workflow.
- Preserved existing template assets, GUIDs, and recovery files.

Git baseline references: `0fcd889` (initial check-in), `bbcf116` (initial test coverage). The detailed early implementation sequence is also recorded in the historical development log.

## 2026-08-30 — Player movement, input, and camera

### Added

- Rigidbody2D-based top-down movement with collision and normalized diagonal input.
- Keyboard and gamepad movement through identified movement intentions.
- Independent 360-degree aim using absolute mouse/world position or the gamepad right stick.
- A player-following camera with fixed orientation.
- Aim presentation that follows accepted gameplay state.
- Pause, Resume, focus-loss handling, and input gating across application states.
- Safeguards against held movement and attacks leaking out of menus, pause, or focus changes.
- Automated keyboard, mouse, and simulated-gamepad coverage, plus recorded Editor/Windows smoke checks.

Physical gamepad hardware testing remained outstanding. The later room-transition movement fix narrows the release requirement for ordinary room loads while preserving pause/focus safeguards.

## 2026-08-30 — Initial weapons and combat

### Added

- Static weapon definitions and immutable runtime weapon configurations with stable identities.
- Knife melee attacks using an aimed forward attack region.
- Blaster attacks using traveling projectiles.
- A single identified Attack intention using the player's accepted aim direction.
- Authoritative validation for player identity, activity, weapon configuration, aim, and cooldown.
- Damage requests/results, target health, duplicate-hit protection, melee/projectile presentation, and health bars.
- Press-based attack handling that does not queue rejected attacks or repeatedly fire simply because a button remains held.
- Combat cleanup when leaving a room or destroying a session.
- Regression coverage for cooldowns, aim, target damage, projectile behavior, and input/UI interaction.

Only Knife and Blaster were implemented. Other weapon concepts did not become completed weapon features.

## 2026-08-30 — Deterministic generation and authored room loading

### Added

- Seeded deterministic graph generation using project-owned random-number generation, stable room identities, and graph fingerprints.
- An initial forward branching/rejoining graph with bounded routes. This original topology was later superseded by the TinyKeep-derived bidirectional layout.
- Data-driven room definitions and six reusable room categories: Starting, Cockpit, Engine, Generator, Lab, and Storage.
- Authored room prefabs with spawn anchors, doorway triggers, and validated room/exit composition.
- Authoritative room progression and identified exit requests, rejecting invalid, stale, duplicated, or out-of-context travel.
- Room replacement, safe player placement, camera updates, physics synchronization, and combat/projectile cleanup during travel.
- Safe failure behavior when room authoring or prefab mappings are missing.
- Seed-invariant, traversal, asset-reference, and build validation.

The original terminal room remained playable without a victory objective. A complete run-ending interaction was added later.

## 2026-08-31 — Early two-player co-op proof

### Added

- The approved minimal Netcode for GameObjects and Unity Transport stack.
- Controlled host/client joining with a two-player limit and distinct player identities/spawns.
- Sender-authenticated movement, aiming, and attack requests routed through the existing simulation boundary.
- Host-owned combat, target health, run seed, room state, and progression, with client presentation and graph agreement checks.
- Shared room transitions, originally requiring both players to use the same exit together.
- Ownership/freshness checks, disconnect handling, input neutralization, and local pause/focus isolation.
- Separate Windows-process proofs using Knife and Blaster, shared split/rejoin traversal, and terminal-room arrival.

Historical validation recorded 122 Edit Mode and 23 Play Mode passing cases. This was an architectural/local networking proof, not Steam integration or production networking certification. Independent co-op room exploration was added later.

## 2026-10-20 — Regression repair and legacy naming cleanup

### Fixed

- Corrected unfocused batch-test UI dispatch while preserving real pointer delivery.
- Corrected a camera test to compare its presented target with an appropriate floating-point tolerance.
- Added clearer pointer-delivery checks and timeout diagnostics.

### Changed

- Renamed 19 development-era assets and updated affected runtime types, Editor utilities, UI references, and launch options to functional names.
- Removed development-stage wording from player-facing menus and HUD.
- Preserved existing GUIDs, serialized references, seed values, Core behavior, packages, historical evidence, and recovery material.
- Updated networking compatibility checks for the renamed implementation without changing the gameplay loop.

Git references: `4acbb68`, `979469c`.

## 2026-10-20 — Three-level runs and Save/Continue

### Added

- Three-level run sequencing, initially with 10–17 total rooms per level, including branches.
- Advancement from levels 1 and 2 while retaining a playable level-3 terminal room.
- Room-entry checkpoints recording seeds, generation identity, graph fingerprints, level/room history, and equipment.
- Continue for solo and host-owned co-op runs.
- Co-op readiness gating before enabling shared gameplay after load.
- Versioned atomic save replacement, backups, compatibility validation, and feedback for invalid saves.
- Preservation of invalid/incompatible data instead of deleting it silently.

The 10–17 range was later replaced by 10–25. Later persistence work also expanded the saved player/enemy state beyond this initial checkpoint design.

Git reference: `943dad1`.

## 2026-10-20 — Enemy combat foundations

### Added

- Room-scoped enemy identities, health, faction filtering, and duplicate-hit protection.
- Host-owned enemy death/despawn state with client presentation only.
- Authored spawn slots, encounter registration, and room cleanup.
- Enemy state replication and corresponding session compatibility changes.

Git reference: `100f058`.

## 2026-10-21 — Player health, death, revival, and profile rewards

### Added

- 100 starting maximum health, retained across rooms and Continue.
- Solo death and party-wipe handling that ends the run and disables Continue for it.
- Co-op downing and nearby teammate revival using a three-second Interact hold.
- Revival at 50% of the downed player's maximum health, with interruption/input-release safeguards.
- Run-XP and class-identity extension points, separate from Unity presentation.
- Permanent profile XP awarded at death or victory using a 6% conversion of shared run XP, with fractional credit carried forward.
- Durable end-run receipts, duplicate-reward protection, crash recovery, and reward retry handling.
- Separate profile storage, backward-compatible save loading, health UI, and death-screen navigation.

Enemy-kill XP was not yet a gameplay reward source at this point; it was implemented later. Class identity fields were foundations, not selectable classes or class abilities.

Git reference: `7a5756d`.

## 2026-10-21 — Melee enemy AI and navigation

### Added

- Host-controlled melee enemy pursuit, target selection, attack windup, strike, recovery, disengagement, and return behavior.
- A* 2D navigation with obstacle clearance and line-of-sight/attack obstruction checks.
- Replicated enemy movement and attack presentation.
- An initial moving encounter in nonterminal Engine rooms, preserving safe starting/final rooms and existing exit rules.

### Fixed

- Removed three stationary diagnostic targets from each playable room prefab, keeping them only in explicitly enabled test fixtures.
- Preserved the functioning moving enemy rather than rebuilding its combat system.

At this point, Continue preserved player health but reset enemies. Persistent enemy state was added in the next traversal update.

Git references: `a94f200`, `6e1612a`.

## 2026-10-21 — TinyKeep-derived layouts and persistent bidirectional travel

### Changed

- Replaced the original forward-only layout with a TinyKeep-derived level topology.
- Expanded each level to 10–25 total rooms, including branches, with at most four connections per room.
- Randomized room order and connectivity while retaining deterministic seeds and fingerprints.
- Added bidirectional room travel and reciprocal doorway arrival placement.

### Added

- Persistent enemy health, position, and death state across revisits and Continue.
- Saved player position, aim, health, equipment/cooldown, XP, and identity.
- Five-second dirty autosaves and transition saves, with safe storage/recovery and legacy-save compatibility.
- Reconstruction of saved traversal and room state without serializing unstable Unity object references.

Room interiors remained reusable authored content, not procedurally generated interior geometry.

Git reference: `e141669`.

## 2026-10-21 — Run victory and completion results

### Added

- A gold completion marker in level 3's final room.
- Solo completion through Interact, and co-op completion requiring both players alive, nearby, and independently confirming.
- Rejection of premature completion; downed players must be revived before the party can finish.
- Exactly-once completion rewards, invalidation of Continue for the finished run, and completion/reward presentation with Return to Main Menu.
- Backtracking until the party commits completion.

No boss encounter or new mandatory kill-to-exit rule was added.

Git reference: `f2205e8`.

## 2026-10-23 — Steam integration and account-linked local profiles

### Added

- Steamworks.NET integration, Steam initialization/account checks, private lobby/session hosting, invitation handling, and Steam networking transport.
- Session/build/protocol admission checks, status/error presentation, and cancellation/leave handling.
- Host-owned saved co-op continuation tied to the original host and teammate.
- Separate progress and saves for different Steam accounts.
- Explicit linking of existing local profiles to Steam identity, preserving XP/saves and making a backup first.
- Account-change protection that blocks further play until restart under the appropriate account.
- Offline solo/local-save support.

### Validated and limited

- Added platform-service tests, local process trials, Steam initialization/session checks, and Windows build validation.
- Investigated Editor assembly-copy/file-lock problems and recorded successful compilation, domain reload, and Boot-to-Main-Menu validation without changing previously tested gameplay source.
- Profiles remained local. Steam Cloud, public matchmaking, achievements, host migration, and Steam uploads were not implemented.
- Remote co-op with two separate Steam accounts remained a pending friend/beta test, not a completed validation claim.

The Steam/profile checkpoint bundles several related integration iterations; its commit date is not a claim that all integration work occurred in one day.

Git reference: `7f238d8`; additional evidence under `Logs/Validation/Steam`, `SteamSessions`, and `EditorCompilation`.

## 2026-10-23 — HUD, combat feedback, and usability

### Changed

- Refined the HUD with player/teammate health, revive progress, and run XP.
- Improved enemy attack warnings and hit feedback.
- Clarified Steam/profile/save messages and hid technical diagnostics by default.
- Preserved gameplay rules, save compatibility, metadata, and recovery files.

Git reference: `b02d0b4`.

## 2026-10-23 — Audio cues and persistent audio options

### Added

- Fourteen original placeholder sounds covering combat, enemies, damage, revival, transitions, run outcomes, and UI.
- Main Menu/Pause audio options: master, effects, UI volume, mute, and restore defaults.
- Device-local preference storage separate from profiles and run saves.
- A bounded sound pool and event-based presentation that avoids replaying sounds from repeated network snapshots.
- Options navigation that returns to the originating menu without unintentionally resuming gameplay.

Co-op pause/options remained local; they do not freeze the other player or the host's shared simulation. Final audio production and a full graphics/display settings suite were not added.

Git reference: `6608e34`.

## 2026-10-23 — Difficulty, safe rooms, shared minimap, independent exploration, and sprint

### Added

- Easy / Normal / Hard choices, initially using 0.75 / 1 / 1.5 factors for enemy health, damage, and encounter size, bounded by authored spawn capacity.
- Safe starting rooms and difficulty-driven ordinary-room encounter composition.
- A shared explored-room minimap that reveals entered rooms only, with distinct player location colors.
- Independent co-op exploration of different rooms on the same level.
- Shared level progression requiring both players to regroup at the level exit.
- Left Shift sprint at 1.5 times normal speed, without stamina.
- Save/Continue support for difficulty, exploration, each player's room/position, and persistent enemy state.

### Changed

- Replaced the original same-room/shared-door co-op traversal restriction for ordinary room travel. Players still cannot occupy different levels.
- Left enemy movement speed and attack timing unchanged by difficulty multipliers.

Git reference: `0895652`.

## 2026-10-23–24 — Ranged encounters and enemy-kill XP

### Added

- A ranged enemy archetype with preferred-distance repositioning, a locked-direction telegraph, traveling hostile projectiles, and recovery between shots.
- Host-only ranged decisions and projectile damage, with client state/presentation and room-aware cleanup.
- Cover/dodge validation and safeguards against damaging players in unrelated rooms or replaying projectile damage.
- Seeded difficulty-dependent melee/ranged encounter composition, while preserving safe starting and terminal rooms.
- A host-authoritative enemy-kill reward source: base 10 XP multiplied by enemy, level, and difficulty factors, rounded per kill.
- Shared co-op run XP, including eligibility for downed teammates, with the existing 6% profile conversion at death/victory.
- Kill identity accounting and persistence checks preventing duplicate XP from revisits, repeated death events, or Continue.
- Saved immutable reward tuning and compatibility behavior that leaves older runs on their prior reward rules.
- Additional ranged-combat, reward, save/resume, and two-process validation, plus updated Windows testing packages.

Ranged work and kill rewards were recorded together in the October 24 Git checkpoint. These changes did not add class-specific progression, loot, or stat bonuses.

Git reference: `97e57c2`; evidence under `Logs/Validation/RangedCombat` and `KillRewards`.

## 2026-10-24 — Ranged enemy health-bar repair

### Fixed

- Corrected identical purple health-fill/background colors that made the bar appear unchanged while damage was being applied.
- Restored a green fill over a dark background, retaining the purple ranged-enemy body.
- Corrected authoring code to tint only the body, preventing regenerated prefabs from recreating the issue.
- Added regression checks for contrast, authoritative/replicated health changes, restored partial health, and an empty bar on death.

Validation recorded 244 Edit Mode and 85 Play Mode passes, a successful Windows build, and four local two-process combat/resume scenarios. Existing packaged builds were not replaced by this repair alone; subsequent builds include it.

Git reference: `002e309`.

## 2026-10-24 — Class and difficulty selection modal

### Added

- A New Game modal with difficulty selection and six class identities: Pilot, Mechanic, Doctor, First-Mate, Logistics Manager, and Captain.
- A required class selection before starting, with Start/Join and Cancel actions.
- Individual co-op class choices, with difficulty controlled by the host.
- Saved per-player class selections using existing run-state/save extension points.
- Continue behavior that restores the saved selections rather than overwriting them with new menu choices.
- Stable Unity-independent class identities, class immutability checks, and updated co-op handshake/admission compatibility.
- Modal cancellation, class persistence, neutral-stat, co-op restore, and multi-resolution UI validation.
- Refreshed playable and packaged Windows builds.

Class selection adds identity only. Abilities, unique stats, class-specific XP/levels, equipment restrictions, and perks were not implemented. The save schema remained 7; session compatibility advanced to protocol 12.

Validation recorded 250 Edit Mode and 87 Play Mode passes, four local two-process combat/resume checks, and modal checks at four resolutions.

Git reference: `1910792`.

## 2026-10-24 — Held movement through room transitions

### Fixed

- Removed the requirement to release and repress movement controls after an ordinary room transition.
- Preserved live held movement and sprint while room loading temporarily gates gameplay commands.
- Continued reading current device state rather than replaying a cached movement vector, so releasing controls during loading stops movement after entry.
- Preserved movement-release safeguards after pause, focus loss, account changes, and run-ending states.
- Preserved fresh-press requirements for attacks and interactions.
- Made repeated room-availability notifications idempotent and retained continuity through nested room/level-entry handling.

### Tested

- Added keyboard, gamepad, diagonal/sprint, release-during-loading, interruption, and host/client room-transition regressions.
- Synchronized physics assertions with physics ticks and tightened simulated-input cleanup in the test fixtures.
- Added a development-only two-process movement check and corrected its peer timing assumptions.
- Final full suite: **250 Edit Mode + 91 Play Mode tests passed — 341 total**.
- A two-Windows-process scenario passed for both players retaining held movement/sprint through their own room transitions and stopping after release.
- Windows Development build succeeded with zero errors and one existing navigation API deprecation warning.
- Refreshed the local playable build and generated a verified testing ZIP.

Movement speed, save format, multiplayer message layout, and enemy/gameplay balance were unchanged.

Git reference: `8fae769`.

## 2026-10-24 — Documentation and handoffs

- Created an updated gameplay-loop diagram in the original diagram's style, showing the implemented three-level, save/continue, death/revival, completion, and profile-reward flow as it stood when drawn.
- Prepared an implementation handoff for another agent writing a press release, explicitly distinguishing implemented functionality, pending designs, and validation limitations.
- Compiled this project-wide changelog from Git history, the historical implementation log, owner-approved changes, and validation evidence.

The diagram predates the class-selection modal; its pending-class note reflects that earlier snapshot and should not override the later implementation entries above. Documentation artifacts are outside Unity game assets.

## Current build and validation references

- Playable build: `Builds/WindowsDevelopment/Dungeon-Crawler.exe`
- Latest testing ZIP: `Builds/FriendTesting/5f7838e4a5ef/Dungeon-Crawler.zip`
- ZIP SHA-256: `c99cddecd522db2f02becc86792b9cdbc5e1865f892f22f3bbb6f109e3144c9e`
- Latest validation report: `Logs/Validation/RoomMovement/Results.json`
- Implementation/press-release handoff: `Logs/Validation/PressRelease/ImplementationHandoff-2026-10-24.md`
- Historical early implementation evidence: `Assets/_Project/Docs/DevelopmentRoadmap.md` — historical, not the current feature-status authority.
- Current save schema: 7. Current session protocol: 12.

## Pending or explicitly deferred — not completed changes

- Distinct class abilities, stats, equipment, class-specific permanent XP/leveling, and unlocks.
- Upgrade types, stacking rules, special upgrade rooms, and/or between-level upgrade choices/screens.
- Yellow next-level doors matching the existing gold level-3 completion marker.
- The proposed additional combat-readability redesign; it was tabled until classes and upgrades are defined.
- More weapons/enemies, loot/equipment economy, bosses, and substantial narrative/art content.
- Public matchmaking, achievements, Steam Cloud, host migration, dedicated servers, or more than two players.
- Remote two-account Steam co-op/friend-invitation beta validation and physical controller hardware certification.
- Production release certification, final performance targets, release dates/pricing, and additional supported platforms.

These are outstanding design/validation areas or explicitly excluded scope, not commitments to implement every item. No deferred proposal should be treated as implemented merely because it appears in an older roadmap or conversation.
