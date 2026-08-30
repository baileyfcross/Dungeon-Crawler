# Development Roadmap

Status: Phase 1 is approved and complete. Phase 2 was authorized before implementation on 2026-08-30 and is now implemented and validated, ready for owner review. Work stops before Phase 3. The foundation, existing packages, fixed top-down URP 2D direction, and early Phase 5A multiplayer spike are preserved. No weapons, combat, real rooms, generation, saves, inventory, multiplayer, Steam, or additional packages were implemented or installed.

Intake date: 2026-08-30.

## Project goal and scope

Build a small, production-quality space-themed top-down dungeon crawler with deterministic procedural rooms, melee and ranged weapons, and exactly one attack command determined by the equipped weapon.

Approved design vocabulary:

- Melee weapons: Knife and Tree. Tree is intentional and must retain its name.
- Ranged weapons: Blaster, Long Blaster, and Shotgun Blaster.
- Room categories: Starting, Engine, Cockpit, Storage, Generator, and Lab.
- Flow: Main Menu -> New Game -> Starting room -> room exits and subsequent rooms. Continue restores the saved run and room. Death opens a death screen whose actions still need definition.
- Main menu: Continue only for a valid save, New Game, Load Game, Options, Exit.
- Pause menu: Resume, Options, Exit to Main Menu.
- Options: volume, resolution, Fullscreen, Borderless, Windowed; preferences stored separately from run saves.

Do not add progression, loot, currencies, classes, crafting, enemy types, death penalties, or room-specific effects without a design decision. Validate multiplayer architecture in a limited two-player technical spike after deterministic generation and basic room transitions, before larger save/content systems. Production multiplayer and Steam remain deferred. Do not install multiplayer/networking packages during the initial foundation phases.

The final direction is fixed top-down 2D using the existing URP 2D setup. NSEW describes that fixed world view; it does not mean rotating the camera between cardinal viewpoints or coupling movement to aiming. The camera follows/centers on the controlled player while maintaining a fixed orientation, independent of movement, facing, or aiming. Keyboard/mouse aiming uses the cursor's world-space position relative to the player for continuous 360-degree aim, not pointer delta. Gamepad right-stick aim uses the same aim-intention boundary. These ambiguities are resolved and their Phase 2 implementation is complete.

## Intake evidence and Phase 0 validation

The initial intake inspected project files, resolved package metadata in Library/PackageCache, saved scenes, editor session metadata, the running Unity executable, and the editor log. At that point no callable Unity Editor integration was available, so the first report could not verify live hierarchy, Console counts, or the active Build Profile. The subsequent live validation below resolves those baseline gaps without adding Editor scripts or packages.

Library/EditorInstance.json identifies the project's running editor as Unity 6000.3.21f1. Library/LastSceneManagerSetup.txt and the editor log identify SampleScene as the last recorded active scene. The live window inspection independently confirmed that project, editor version, and scene.

### Live Editor baseline validated on 2026-08-30

Validation used read-only inspection of the actual Unity windows through Windows native menus/window capture, corroborated by project files and logs. The sandbox could not see the Editor window; the permitted host-side inspection could. This establishes the visible live baseline, not a callable Unity API integration or a Play Mode/build test.

| Check | Confirmed live state |
| --- | --- |
| Editor / project | Responsive Dungeon-Crawler Editor, Unity 6.3 LTS 6000.3.21f1, DX12. ProjectVersion.txt records revision c02631ffc030. |
| Scene / hierarchy | SampleScene open, with Main Camera and Global Light 2D only. No unsaved-change marker visible on the scene/title. |
| Editor mode | Play Mode inactive. No Play Mode session or forced compilation was initiated during validation. |
| Console | At 02:57, 03:00, and the 03:06 clarification recheck (America/New_York): 0 errors, 0 warnings, 0 messages; search field empty. The Console was not cleared or its filters changed by this validation. |
| Active build target | Windows platform, Intel 64-bit, Build and Run on Local Machine. Windows Server is not the active platform. |
| Active profile | Built-in Windows platform configuration using the shared scene list; no custom Build Profile assets or Development/Release profile pair. Development Build is unchecked. This is not an authored Release profile. |
| Build scene list | Scenes/SampleScene enabled at index 0, corresponding to Assets/Scenes/SampleScene.unity. |
| Project state | Template assets plus the roadmap and Unity-generated metadata; no gameplay scripts, prefabs, menus, generation, saves, project tests, or project assemblies added. Existing uncommitted settings changes preserved. |

File checks reconfirmed URP 17.3.0 with Renderer2D, Input System 1.20.0 as the sole input backend, Force Text serialization, Visible Meta Files, and matching cached/locked package versions. No multiplayer packages were installed. Historical startup diagnostics remain in Editor.log; they are not current Console entries. No tests, builds, settings changes, scene edits, or Phase 1 implementation were performed.

After the owner's fixed top-down 2D/NSEW clarification, live window inspection at 03:06-03:07 America/New_York reconfirmed the same Editor/scene/Console baseline and Windows Intel 64-bit platform configuration, shared SampleScene list, no custom profiles, and Development Build off. The design clarification changes documented requirements only; it does not change the current scene, input asset, or camera behavior.

### Intake findings (historical Phase 0 baseline)

| Area | Observed state |
| --- | --- |
| Unity | 6000.3.21f1, revision c02631ffc030; Unity 6.3 LTS. ProjectVersion.txt, editor metadata, and executable path agree. |
| Project maturity | Essentially a fresh Universal 2D template, with eight non-meta content files before this document. No game implementation found. |
| Rendering | URP 17.3.0, using Assets/Settings/UniversalRP.asset and Renderer2D.asset. All six quality levels reference that pipeline asset. GraphicsSettings has no default pipeline assigned, so the quality overrides are important. Linear color space; editor default behavior is 2D. |
| Input | Input System 1.20.0 only (activeInputHandler = 1). The template InputSystem_Actions asset is assigned as project-wide actions. Legacy InputManager.asset exists but is not the selected backend. |
| Scenes | Assets/Scenes/SampleScene.unity and Assets/Settings/Scenes/URP2DSceneTemplate.unity. Only SampleScene is enabled in the shared build scene list. The latter scene is a template dependency. |
| Hierarchy | Live SampleScene confirms Main Camera and Global Light 2D only; both saved scenes contain these two roots. The saved camera is orthographic, size 5, at (0, 0, -10), with AudioListener and UniversalAdditionalCameraData. The light uses the package Light2D component. No player, room, UI, or gameplay objects. |
| Gameplay scripts / namespaces | No project C# scripts or managed plug-ins in Assets; no project namespaces or established gameplay architecture. Scene script GUIDs resolve to URP package components. |
| UI | uGUI 2.0.0 and built-in UI Toolkit support are available. No Canvas, UIDocument, UXML, USS, menu controllers, or project UI assets were found. Installed UI packages do not establish a chosen UI architecture. |
| Save system | None found. No save DTOs, persistence code, save selection, or Continue validation. |
| Procedural generation | None found. No generator, graph model, room definitions, or room prefabs. |
| Console / compilation evidence | Live Console: 0 errors, 0 warnings, 0 messages with an empty search field. No C# diagnostics or compilation-failure markers found in the inspected editor log. Historical URP Terrain/Lit warnings, a D3D12 info-queue diagnostic, and licensing-handshake errors followed by recovery remain in that log; their runtime impact was not tested. No forced compilation was performed. |
| Tests | Unity Test Framework 1.6.0 is installed; no project Edit Mode or Play Mode tests exist. Package tests are not project tests. |
| Assembly Definitions | No project .asmdef or .asmref files. Package assemblies exist independently. |
| Build Profiles | Live Build Profiles confirms Windows active, Intel 64-bit, using the shared scene list. Development Build is off. No custom profile assets in Assets and no Development/Release pair. This live result supersedes the initial ambiguity from cached Library profile data. |
| Design overlap | URP 2D rendering, an orthographic camera, project-wide Player/UI input actions, and testing infrastructure are present. None of the specified gameplay or menu flows are implemented. |

### Existing folder structure

Before adding this documentation, the content tree was:

```text
Assets/
    DefaultVolumeProfile.asset
    InputSystem_Actions.inputactions
    UniversalRenderPipelineGlobalSettings.asset
    Scenes/
        SampleScene.unity
    Settings/
        UniversalRP.asset
        Renderer2D.asset
        Lit2DSceneTemplate.scenetemplate
        Scenes/
            URP2DSceneTemplate.unity
```

Every existing content asset had a .meta file. This audit adds only Assets/_Project/Docs/DevelopmentRoadmap.md and its parent documentation folders; Unity must generate any new metadata. No existing content was moved or renamed.

### Relevant installed packages

Versions were checked against packages-lock.json and local package.json files; no cached-version mismatches were found.

| Package | Version | Purpose / decision |
| --- | --- | --- |
| com.unity.render-pipelines.universal | 17.3.0 | Existing render pipeline; preserve. |
| com.unity.render-pipelines.core / com.unity.shadergraph | 17.3.0 each | Existing rendering dependencies. |
| com.unity.inputsystem | 1.20.0 | Retain for gameplay and UI commands. |
| com.unity.ugui | 2.0.0 | Installed; no project UI to replace. |
| com.unity.modules.uielements | 1.0.0 | Built-in UI Toolkit module, versioned with the editor. |
| com.unity.test-framework | 1.6.0 | Use for project Edit Mode / Play Mode tests. |
| com.unity.test-framework.performance | 3.5.0 | Transitive dependency; installed, not a configured profiling suite. |
| com.unity.ext.nunit | 2.0.5 | Test dependency. |
| com.unity.2d.animation | 13.0.5 | Template package. |
| com.unity.2d.aseprite | 3.0.2 | Template package. |
| com.unity.2d.psdimporter | 12.0.2 | Template package. |
| com.unity.2d.spriteshape | 13.0.0 | Template package. |
| com.unity.2d.sprite / com.unity.2d.tilemap | 1.0.0 each | Built-in 2D tools. |
| com.unity.2d.tilemap.extras | 6.0.2 | Template package. |
| com.unity.2d.common / com.unity.2d.tooling | 12.0.3 / 1.0.3 | 2D dependencies/tooling. |
| com.unity.burst / com.unity.collections / com.unity.mathematics | 1.8.30 / 2.6.8 / 1.3.3 | Dependencies already present; no ECS architecture implied. |
| com.unity.multiplayer.center | 1.0.1 | Editor tooling, not an implemented networking stack. |
| com.unity.timeline / com.unity.visualscripting | 1.8.12 / 1.9.12 | Installed but no authored gameplay found. |
| com.unity.collab-proxy | 2.12.4 | Unity Version Control integration; repository currently uses Git. |
| com.unity.ide.rider / com.unity.ide.visualstudio | 3.0.40 / 2.0.26 | Editor integrations. |

Cinemachine, Addressables, Netcode for GameObjects, Unity Transport, the Multiplayer Services SDK, Multiplayer Play Mode, and Steam integration packages were not found in the resolved dependency list. Do not install them as part of intake. Retain existing packages until there is a reason to change them. In particular, add no multiplayer/networking packages during the initial foundation phases; defer spike-specific package selection and installation until Phase 5A and its scope/stack approval gate. The already installed Multiplayer Center does not constitute a runtime networking implementation.

### Input details

- Player actions: Move, Look, Attack, Interact, Crouch, Jump, Previous, Next, Sprint.
- Move includes WASD/arrows and gamepad left stick. Attack includes mouse left button, Enter, and gamepad west button. There is already one Attack action.
- The existing template Look action uses pointer delta and gamepad right stick. Mouse aim semantics are now resolved: absolute mouse position determines a 360-degree world-space direction on the 2D gameplay plane, independently of movement. Adapt the actions in Phase 2; do not use mouse delta as the primary aiming model. The input asset is unchanged during Phase 0.
- Interact uses a Hold interaction. Confirm whether room interaction should require a hold.
- There is no Player/Pause or Player/Aim action.
- UI already has Navigate, Submit, Cancel, Point, and Click, plus template extras.
- Control schemes: Keyboard&Mouse, Gamepad, Touch, Joystick, XR. Their presence is not a commitment to ship on all of those devices.
- Wrapper-code generation is disabled and there is no gameplay consumer. Do not implement jump, sprint, crouch, or weapon switching merely because template bindings exist.

### Team configuration and existing work

- Asset Serialization is Force Text; Version Control Mode is Visible Meta Files. Preserve both.
- Git ignores Library, Temp, Logs, and UserSettings. The attributes file assigns Unity YAML merging and LFS rules for media.
- The local unityyamlmerge merge driver was not configured in the inspected Git configuration. LFS filter-process is configured. Team setup should verify Smart Merge and LFS availability on each workstation; neither was changed during intake.
- Existing uncommitted changes were present in UniversalRP.asset, ProjectSettings.asset, and URPProjectSettings.asset, with PackageManagerSettings.asset untracked. They were left intact. Their contents include URP serialization-version changes and project/cloud identity settings; authorship was not assumed.
- Player settings retain template identity values, including DefaultCompany. Release identity, desktop scripting backend, display behavior, and supported platforms require deliberate profile configuration later.

## Current architecture and implemented systems

The Phase 1 application flow and Phase 2 local player are implemented in two runtime assemblies. Plain-C# application/player rules and intention/state data live in SpaceCrawler.Core; scene composition, input, Rigidbody2D physics, camera, and UI/presentation adapters live in SpaceCrawler.Gameplay. The existing URP/Input System setup and input asset GUID remain. Combat, generated runs, saves, and network authority are not implemented.

## Architecture direction (approved; implemented through Phase 2)

1. Use Assets/_Project for new project-owned content, following the requested Art, Audio, Data, Materials, Prefabs, Scenes, Scripts, Settings, Tests, and Docs grouping as each area becomes necessary. Leave existing template assets in place unless an approved change requires otherwise.
2. Start with two runtime assemblies: SpaceCrawler.Core for plain C# state, deterministic generation, and save models; SpaceCrawler.Gameplay for Unity-facing adapters and composition. Keep UI, Audio, Save, Platform, Player, Combat, and Rooms in focused folders/namespaces. Use separate Edit Mode and Play Mode test assemblies. Split UI into an assembly only when it improves a real dependency boundary. Gameplay depends on Core; Core must not depend on Unity presentation.
3. One explicit composition root constructs run services and supplies serialized/constructor dependencies. Use explicit application states (Boot, MainMenu, Loading, Gameplay) and gameplay states (EnteringRoom, Playing, LeavingRoom, Paused, PlayerDead). Avoid mutable global state and scene searches.
4. Keep boot/menu concerns separate from a persistent gameplay scene. The gameplay scene contains player instances, local camera/presentation, and a room container; authoritative run/player state belongs to gameplay systems, not scene presentation. RoomLoader replaces reusable room prefab instances within that scene. Do not introduce a streaming system. Application/session dependencies are handed to scene adapters explicitly; avoid an implicit global "the player" dependency.
5. Separate player input, motor, health, weapons, and presentation. Use the approved fixed top-down URP 2D direction: keyboard movement is in the XY gameplay plane, independent of aiming; the camera follows the player's resulting position with a fixed orientation. Convert absolute mouse position to a point on the 2D world plane and derive the player-to-pointer aim direction over the full 360 degrees. Do not use mouse delta as the primary aim model or rotate the camera between cardinal views. Input adapters translate keyboard/mouse and gamepad input into gameplay commands/intentions, including exactly one Attack command that uses the aim direction. They request actions rather than directly changing authoritative position, health, cooldowns, equipment, or run/room state. Gameplay systems validate and apply requests; presentation consumes resulting state. [Unity's Input Actions](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.20/manual/Actions.html) support the separation of intent and device bindings.
6. WeaponDefinition and RoomDefinition ScriptableObjects contain stable IDs and static configuration. Equipped-weapon state, cooldowns, health, and run progress belong to runtime objects. Use reusable melee and projectile attack behaviors, adding only configuration fields needed by the current slice. Preserve all five named weapons without inventing acquisition systems.
7. RunGenerator produces DungeonGraph / RoomInstanceData independently of GameObject creation. Specify a PRNG and stable definition ordering so generation does not depend on UnityEngine.Random global state or unordered collections. Record stable run-local room IDs. Persistence must account for generator/content changes through versioning or sufficient stored graph data; a seed alone is not a cross-version reconstruction guarantee.
8. Use a versioned save DTO and a separate storage boundary under Application.persistentDataPath. Save only implemented state. Use stable definition IDs, validate before offering Continue/Load, and write via a temporary file with safe replacement/recovery appropriate to the target platform. Treat room transitions as checkpoint boundaries, with exact checkpoint timing decided before implementation.
9. Prefer UI Toolkit for menus using UXML, USS, and presenters. Add menu focus, controller navigation, and gameplay/UI input gating early. [Built-in UI Toolkit](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-Runtime-Event-System.html) can use the active Input System backend.
10. Persist options separately from saves. Use a master AudioMixer route when adding audio and volume controls; no mixer exists yet. Keep resolution/display APIs and eventual Steam integration in platform adapters. Consider Cinemachine 3 only after the camera requirements justify it. Use Awaitable/cancellation for real loading workflows and UnityEngine.Pool only for justified repeated projectile/effect creation.

### Authority boundary from the foundation

Use this direction from the first gameplay implementation:

```text
Input
-> gameplay command/intention
-> simulation/gameplay system
-> resulting state
-> presentation
```

- Initially, requests execute against a local simulation without networking packages. Keep the request boundary explicit and small; ordinary method calls and focused command data are sufficient. Do not build a generic command bus, replay framework, or speculative networking abstraction.
- Associate gameplay requests with an explicit player identity/context. Simulation/gameplay systems own movement rules, attack validation, cooldowns, damage, and run/room transitions. Input and UI cannot bypass those systems to author authoritative state.
- Movement intention and world-space aim intention are independent. The single Attack request uses that aim direction through Input -> Command -> Simulation -> State -> Presentation. Neither pointer input nor the following camera directly resolves attacks or damage.
- Presentation observes resulting state/events and handles visuals, audio, and the local camera. Visual ownership does not confer authority over gameplay. A motor may use Unity physics as part of the simulation; it is not an input-owned transform writer.
- The later spike adds the minimum session/transport adapter needed to deliver requests to one agreed authority and distribute resulting state. Local and remote players must use the same gameplay rules, rather than separate authoritative implementations.
- One authority owns the procedural run seed, generated run information, and current room/transition state. Clients may reconstruct layout from agreed generation data but must not independently choose a competing seed or advance the run.
- Deterministic room generation does not imply deterministic physics, lockstep simulation, or a requirement to build prediction/rollback now.

## Phase 1 implementation and validation — 2026-08-30

### Implemented scope and architecture

The owner approved Phase 1 only. The resolved URP 2D, fixed-orientation camera, independent movement/aim, absolute world-space mouse aiming, and eventual directional-stick gamepad aiming requirements were recorded before implementation. None of the Phase 2 player/input/camera behavior was implemented.

The application flow is deliberately small:

```text
UI input -> ApplicationCommand -> ApplicationFlow rules -> ApplicationState -> Unity scene/UI presentation

Boot -> Loading -> MainMenu -> Loading -> Gameplay placeholder -> Loading -> MainMenu
MainMenu -> Exiting
```

- `ApplicationFlow` is plain C# with private state mutation. It accepts valid commands, rejects out-of-context/repeated requests while loading, validates completion destinations, and restores the source state on loading failure. `Exiting` is the only additional state beyond the four required states, to represent the approved Exit action explicitly.
- `ApplicationRoot` is the one Boot composition root. It constructs the flow, persists across scene changes, loads explicitly serialized scene paths, instantiates explicitly referenced UI prefabs, and injects the flow into each screen. Scene loading is asynchronous, and teardown cancels abandoned continuations. No runtime singleton, global mutable state, service locator, dependency framework, or scene-wide lookup is used.
- `ApplicationScreen` translates UI actions into commands and observes resulting state. It never assigns application state or performs scene loading. Loading disables further transition buttons. Scene teardown removes the screen's event subscriptions.
- UI Toolkit supplies Main Menu and the clearly labeled Gameplay placeholder. New Game, Return to Main Menu, and Exit work. Continue, Load Game, and Options are visibly disabled with explanatory labels/tooltips. There is no save-system stub pretending to load a run.
- Boot, MainMenu, and Gameplay use fixed orthographic cameras and the existing renderer. These cameras only render the foundation screens; no follow, movement, aim, or combat behavior was added. Menu/gameplay screen prefabs are instantiated by the composition root, so normal entry is through Boot.
- All scenes, prefabs, PanelSettings, and the Build Profile were authored by Unity Editor APIs. No Unity YAML was manually authored. SampleScene and all template assets were retained.

### Files and assembly boundaries

Paths below are under `Assets/_Project/`; Unity-generated `.meta` files accompany every new file/folder.

| Area | Created files/assets |
| --- | --- |
| Core | `Scripts/Core/ApplicationCommand.cs`, `ApplicationState.cs`, `ApplicationFlow.cs`, `SpaceCrawler.Core.asmdef` |
| Unity adapters/composition | `Scripts/Gameplay/ApplicationRoot.cs`, `ApplicationScreen.cs`, `SpaceCrawler.Gameplay.asmdef` |
| Scenes | `Scenes/Boot.unity`, `MainMenu.unity`, `Gameplay.unity` |
| UI prefabs | `Prefabs/UI/MainMenu.prefab`, `GameplayPlaceholder.prefab` |
| UI Toolkit | `UI/MainMenu.uxml`, `GameplayPlaceholder.uxml`, `Foundation.uss`, `FoundationTheme.tss`, `FoundationPanel.asset` |
| Build Profile | `Settings/BuildProfiles/WindowsDevelopment.asset` |
| Edit Mode tests | `Tests/EditMode/ApplicationFlowTests.cs`, `SpaceCrawler.Tests.EditMode.asmdef` |
| Play Mode tests | `Tests/PlayMode/ApplicationSceneFlowTests.cs`, `SpaceCrawler.Tests.PlayMode.asmdef` |
| Editor utilities | `Editor/FoundationAssets.cs`, `FoundationValidation.cs`, `FoundationTestRecorder.cs` |
| Documentation | Updated this roadmap; no empty future-system folders were created. |

Assembly dependency direction:

- `SpaceCrawler.Core`: plain C#, `noEngineReferences: true`; no gameplay/presentation dependency.
- `SpaceCrawler.Gameplay` -> `SpaceCrawler.Core`; Unity-facing scene/UI/composition adapters only.
- `SpaceCrawler.Tests.EditMode` -> `SpaceCrawler.Core` plus the existing test framework; Editor-only.
- `SpaceCrawler.Tests.PlayMode` -> `SpaceCrawler.Core`, `SpaceCrawler.Gameplay` plus the existing test framework. Test assemblies are excluded from the normal player build.
- Authoring/validation utilities use Unity's default `Assembly-CSharp-Editor`. No additional custom editor or future-system assembly was introduced. Their inspection-only object discovery is not used by runtime code.

### Validation results

Validation ran in the live Unity 6000.3.21f1 Editor and in the built Windows player, using native window controls/screenshots and the project Editor utilities.

| Check | Result |
| --- | --- |
| Import/compilation | Passed for Core, Gameplay, both test assemblies, UI assets, and Editor utilities. No remaining C# errors or warnings. |
| Edit Mode | **9 passed, 0 failed, 0 skipped**. Covers transition order, all four commands during loading, invalid completions, failure/retry, Exit, and unsupported/out-of-context commands. |
| Play Mode integration | **1 passed, 0 failed, 0 skipped**. Two complete round trips through actual scenes/UI events; disabled menu controls, input gating during loading, duplicate-request rejection, old-screen destruction, and exactly one persistent root/screen checked. |
| Visible Editor flow | Passed Boot -> Main Menu -> clicked New Game -> Gameplay placeholder -> clicked Return -> Main Menu. Clicking disabled Continue caused no transition. Clicking Exit stopped Play Mode and restored the saved Boot scene. |
| Profile | Active custom Windows Development profile, Windows Intel 64-bit / `StandaloneWindows64`, Local Machine, Development enabled. Its scene override contains Boot, MainMenu, Gameplay in that order. The global SampleScene list is unchanged; no Release/Steam profile exists. |
| Development build | **Succeeded, 0 errors, 0 warnings** using `BuildPipeline.BuildPlayer(BuildPlayerWithProfileOptions)` with the new profile. Initial build: 84.09 seconds, 169,025,184 bytes reported by Unity. |
| Standalone smoke test | Passed at 1280x720: Main Menu rendered; Continue stayed unavailable when clicked; New Game opened the placeholder; Return restored the menu; Exit logged `Exiting` and closed the process. No errors or warnings in the player log. The host initially launched the process hidden; showing its interactive window allowed normal scene completion. |
| Console | Final validation: **0 errors, 0 warnings**; remaining messages are development transition/build/test logs. The reporting utility does not clear the Console; Unity's existing Play/Test behavior can reset displayed entries. |
| Scope/package checks | No movement, aiming, following camera, weapons, generation, rooms, health, enemies, saves, multiplayer, Steam, or Phase 7 features added. Package manifest/lock and template input actions remain unchanged. |

Build output: `Builds/WindowsDevelopment/Dungeon-Crawler.exe` with its sibling data/runtime folders. Keep those folders together when running/copying the build. This is a Development build, not a distribution/Steam deliverable.

Local validation evidence (ignored by Git): `Logs/Phase1/EditMode.xml`, `PlayMode.xml`, their summary files, `Build.summary.txt`, `EditorContinueCheck.txt`, `EditorGameplayCheck.txt`, `EditorReturnCheck.txt`, `EditorExitCheck.txt`, `EditorState.txt`, `PlayerSmoke.log`, and `PlayerExit.json`. The process-close check succeeded; a numeric process exit code was not captured. Native captures also verify the Editor and standalone UI.

### Corrections, preservation, and limitations

- Validation corrected a stale prefab-reference issue in the first Boot authoring pass before gameplay testing. Boot now has one explicitly wired root; running the authoring command again preserves existing assets and does not duplicate it.
- The first Play Mode launch conflicted with the Boot startup-scene override. The test utility now suspends that override while tests run and restores it on completion or launch failure. Temporary test scenes were cleaned up. Incorrect test assumptions about keyboard-submit targeting and informational development logs were fixed, and the full suite was rerun successfully. These were not waived failures.
- Final evidence inspection caught an old test callback overwriting an earlier mode's report. The recorder now has its own matching ScriptableObject script asset, filters by test mode, and immediately unregisters/destroys itself at completion. Both reports were regenerated and checked together after the fix.
- The interrupted test runner temporarily changed `runInBackground`; the original value `0` was restored. The temporary Input System preloaded-asset build entry was removed by its package callback and cleanup was saved. Existing project/cloud identity edits were retained.
- Unity's unrelated services master flag changed during building; it was restored to its original `0` through the Editor serialization API. Existing cloud project identity was not unlinked or replaced. Individual Ads, Analytics, Purchasing, and Cloud Diagnostics reporting settings remain disabled. Temporary restoration/inspection commands were removed from the final utility.
- Unity serialized derived URP shader-prefilter/runtime metadata, the default volume's Bloom filter field, ShaderGraph's default override flag, Standalone batching defaults, and `ProjectSettings/SceneTemplateSettings.json` while authoring/building. These are Editor/package-generated changes, not a renderer replacement or new gameplay feature. URP 17.3.0/Renderer2D, all existing quality-tier pipeline assignments, Input System 1.20.0, Force Text, Visible Meta Files, original scene/input assets, and existing uncommitted changes were preserved.
- Build Profile creation uses one isolated, version-sensitive Editor reflection call because Unity 6000.3 exposes activation/building publicly but keeps the platform-profile factory internal. A future Editor upgrade may require updating that authoring helper or creating the profile through Build Profiles. The saved profile and build call use Unity's normal assets/API; no runtime reflection or new package is involved.
- No architectural deviation or new owner decision is required for Phase 1. The authority boundary is established for application commands only; actual gameplay commands/player identity and the multiplayer proof remain future milestones. This work does not claim network correctness.

### Repeating validation

Use `Tools > Space Crawler > Foundation > Open Boot` for the normal entry scene. `Enter Play Mode` also starts through Boot; `Exit Play Mode` is available if needed. The existing `Create Assets` command fills missing foundation assets without deleting/recreating existing scenes or prefabs.

Use `Tools > Space Crawler > Validation > Run Edit Mode Tests` / `Run Play Mode Tests` while the Editor is idle; wait for completion before starting another run. Results are written under `Logs/Phase1`. `Report State` records scene, profile, application/UI state, and Console counts. `Tools > Space Crawler > Foundation > Build Windows Development` builds the saved profile into `Builds/WindowsDevelopment`.

## Phase 2 implementation and validation — 2026-08-30

Phase 2 was marked authorized before implementation. Baseline: Unity 6000.3.21f1, saved Boot in Edit Mode, WindowsDevelopment active, and 0 Console errors/warnings. Only the approved player/input/camera slice was added.

### Architecture, physics, and presentation

```text
Keyboard/mouse or gamepad
 -> PlayerInputAdapter
 -> MoveIntent / AimIntent (explicit PlayerIdentity)
 -> PlayerSimulation (validation, active gate, speed clamp, normalized aim)
 -> PlayerMotor / Rigidbody2D (collision-resolved movement)
 -> PlayerState
 -> aim presentation and fixed-orientation camera follow
```

- Core uses ordinary C# and System.Numerics.Vector2, without Unity references. Player identity accompanies every MoveIntent/AimIntent. Simulation rejects wrong-player and non-finite requests, normalizes valid aim, and retains the last direction for near-zero aim. Input never writes authoritative position, Rigidbody, Transform, or player state.
- ApplicationRoot instantiates an explicitly referenced GameplaySession after loading Gameplay and supplies local identity 1 and ApplicationFlow. The session wires separate input, motor, camera, and presentation components. No global player, runtime scene search, generic bus, or speculative networking framework was added.
- The configurable speed defaults to 5 world units/second. Input magnitude is capped at one, retaining smaller analog values. PlayerMotor assigns permitted velocity in FixedUpdate and records physics results. Rigidbody2D is dynamic, gravity/damping 0, Z rotation frozen, continuous collision detection, and interpolation. A radius-0.35 CircleCollider2D and zero-friction/zero-bounce material collide with four static BoxCollider2D walls around a 24-by-16 primitive floor. Blocking and wall sliding are tested.
- The orthographic camera directly centers on the interpolated player Transform at Z=-10, size 5, identity rotation; no smoothing, rotation, zoom, shake, or gameplay authority. LateUpdate orders physics-state capture, camera follow, current aim sampling, and aim presentation. A primitive line/triangle consumes resulting aim and is not a weapon.

### Input curation and pause

The canonical Assets/InputSystem_Actions.inputactions retains GUID `2bcd2660ca9b64942af0de543d8d7100`, existing Move/Look/Attack action IDs, and byte-identical importer metadata. The UI map and control schemes are semantically unchanged. The adapter owns a clone of this asset's Player map, not a second input asset or wrapper.

- Move keeps WASD/arrows and gamepad left stick. Unsupported gameplay XR/joystick bindings and template Interact/Crouch/Jump/Previous/Next/Sprint actions were removed; UI navigation remains intact.
- Look was renamed Aim, using absolute Pointer.position and gamepad right stick. PassThrough Vector2 avoids magnitude arbitration between pixel coordinates and stick directions. Aim initial-state callbacks are disabled so re-enabling does not overwrite retained stick aim.
- Every LateUpdate, pointer screen position becomes a ray from the assigned gameplay camera, intersects the XY plane, and subtracts the presented player position. The resulting identified AimIntent becomes normalized world-space state. This runs after camera follow even without a pointer event; near-zero separation retains valid aim.
- Right stick uses the installed Input System's deadzone (0.125 to 0.925). Meaningful pointer/stick activity selects the source; small stick noise/zero retains the last aim. Gameplay rules are device-independent.
- Pause binds Escape and gamepad Start. Attack is reserved, disabled in the owned map, and has no handler or combat behavior.
- ApplicationFlow adds Paused and Pause/Resume without scene reload. UI Toolkit provides Resume, disabled Options, and Return to Main Menu. The existing HUD return remains available; HUD buttons are not keyboard/gamepad navigation targets.
- Pause disables move/aim, clears permitted movement, and stops the motor at the next physics step while UI stays active. Resume skips its activation frame and requires movement/stick controls to return to neutral before fresh input. Focus loss pauses; focus return requires explicit Resume. There is no global timeScale change.

### Files and assets

Paths below are relative to Assets/_Project unless stated otherwise. Unity generated new metadata and serialized assets through Editor APIs; no scene/prefab YAML was authored manually.

| Added | Purpose |
| --- | --- |
| Scripts/Core/PlayerIdentity.cs, PlayerIntents.cs, PlayerState.cs, PlayerSimulation.cs | Identified intentions, state snapshots, gameplay rules |
| Scripts/Gameplay/GameplaySession.cs, PlayerInputAdapter.cs, PlayerMotor.cs, PlayerCameraFollower.cs, PlayerPresentation.cs | Explicit composition and focused Unity adapters |
| Prefabs/Gameplay/ControlsTestArea.prefab | Primitive floor/grid/walls, player, aim marker, camera |
| Art/Materials/ControlsUnlit.mat; Settings/PlayerMovement.physicsMaterial2D | Existing URP unlit shader and frictionless collision |
| Editor/PlayerFoundationAssets.cs | Idempotent fixture creation and Boot wiring |
| Tests/EditMode/PlayerSimulationTests.cs; Tests/PlayMode/PlayerControlsTests.cs | Core and Unity integration coverage |

Changed: Core ApplicationCommand/ApplicationState/ApplicationFlow; Gameplay ApplicationRoot/ApplicationScreen and asmdef; Play Mode test asmdef; Boot and Gameplay scenes; MainMenu.uxml, GameplayPlaceholder.uxml, Foundation.uss; FoundationValidation.cs and FoundationAssets.cs; this roadmap; and the root input asset. Gameplay's placeholder camera is replaced by the session camera. Original template content, existing GUIDs, and pre-existing uncommitted changes are preserved. No packages changed.

### Validation

| Check | Result |
| --- | --- |
| Import/Console | Compiles; final saved Boot baseline has 0 errors and 0 warnings. Console was not manually cleared. |
| Edit Mode | **20 passed, 0 failed, 0 skipped**, including all 9 Phase 1 cases. Cardinal/diagonal/analog movement; independent normalized aim; identity/invalid vectors; physics result ownership; inactive gates; Pause/Resume/return. |
| Play Mode | **4 passed, 0 failed, 0 skipped**, including the unchanged Phase 1 two-round-trip regression. Rigidbody movement, boundary blocking/sliding, cardinal/diagonal/zero pointer aim, stationary pointer with moving player/camera, fixed rotation, virtual right stick/deadzone, pause/UI/focus/neutral-resume. Final suite rerun successfully. |
| Actual Editor | Native OS input verified WASD, diagonals, eight aim directions, independent move/aim, stationary-pointer aim during camera follow, Escape/button pause/Resume, and Return. A 0.4-second sample moved about 2 units cardinally or (1.414, 1.414) diagonally; rightward aim stayed (1, 0) during 2 units of upward movement. |
| Actual focus loss | Held D while minimizing Unity, released it unfocused, then restored: remained Paused with zero velocity. Resume stayed still; fresh D input moved normally. |
| Gamepad | Virtual Input System Gamepad events passed through the actual map/adapter/simulation. **No physical gamepad was connected** (zero outside tests); hardware feel/mappings and disconnect/reconnect are not certified. |
| WindowsDevelopment | Existing Windows Intel 64-bit Development profile and Boot/MainMenu/Gameplay scene list. Build succeeded, **0 errors / 0 warnings**, 169,235,608 bytes, 29.864 seconds. |
| Standalone | Normal interactive launch: New Game, diagonal movement/camera, absolute right/up aim, paused input gating, mouse Resume/HUD Pause/Return, Main Menu Exit. Exit code **0**. |
| Preservation | Input GUID/importer/UI verified; packages and project settings compared with Phase 2 baseline. Temporary preloaded-input cleanup saved; Unity's build-toggled services master flag restored to its original disabled value through Editor APIs. |

Evidence: ignored Logs/Phase2 contains test XML/summaries, EditorState.txt, Manual-*.txt, InputAssetValidation.json, Build.summary.txt, PlayerSmoke.log, PlayerExit.json, and baseline/change inventories. Phase 1 evidence remains in Logs/Phase1. Validation menu commands now write to Logs/Phase2. Actual Editor/standalone screenshots were captured.

### Fixes, limits, and owner decisions

- Fixed initial pointer callbacks replacing retained stick aim on Resume. Synthetic keyboard/mouse tests temporarily bypass Editor Game-view focus filtering and restore it afterward; real OS focus was tested separately. Physics assertions wait for accepted input and fixed steps, not an assumed number of render frames.
- A hidden-window launch/PrintWindow capture path produced native DirectX 12 presentation errors. Retesting the unchanged executable with normal interactive startup and visible-pixel capture did not reproduce them. The first diagnostic log is preserved; no renderer/API setting was changed.
- Native ComputeBuffer disposal/PlayerConnection memory diagnostics still occur at standalone shutdown and also appear in the preserved Phase 1 PlayerSmoke.log. Project code allocates no ComputeBuffers. This inherited renderer/engine shutdown debt remains for investigation; the normal smoke run has no managed exceptions or DirectX presentation errors and exits with code 0.
- No architectural deviation or Phase 2 design decision is outstanding. Direct camera centering, speed 5, neutral-before-resume, and automatic focus-loss pause are bounded implementation choices. Physical controller testing remains a disclosed hardware gap. Local gameplay ownership is validated; network authority/deterministic physics are not claimed.
- Work stops before Phase 3. No weapons, attacks/effects, health/damage, enemies, generation, production rooms, saves, inventory, networking, Steam, Cinemachine, Addressables, or DOTS/ECS were added. The Phase 5A spike still precedes substantial save/content work.

## Current and next milestones

Current milestone: Phase 2 is implemented and validated as reported above, ready for owner review. Continue, Load Game, and Options remain unavailable. Work is stopped before Phase 3.

Next milestone: Phase 3 weapons/basic combat, only after Phase 2 is completed, reported, and separately authorized. This task stops after Phase 2; the conceptual Attack action does not authorize combat implementation.

## Approved phased roadmap

Preserve the approved phase order and add Phase 5A between basic room transitions and Save/Continue/Load. Add basic menu/pause wiring and build validation early, and perform tests/compilation in every phase rather than waiting for Phase 9. Phase 5A is an architectural validation gate, not production multiplayer delivery.

| Phase | Scope | Completion evidence / gate |
| --- | --- | --- |
| 0 - Audit and architecture | Review this intake and amendment, inspect the actual Editor, verify live scene/Console/build target, and record the resolved fixed top-down URP 2D presentation and independent movement/aiming decisions. | Complete: revised roadmap approved and live baseline verified on 2026-08-30, including the recheck after clarification. Final 2D/3D and NSEW decisions are resolved. |
| 1 - Foundation and flow | Add minimal project folders/assemblies, composition root, state model, boot/menu and gameplay scenes. Establish the input/request -> simulation -> state -> presentation boundary with local execution only. Basic New Game/return/Exit navigation; Continue unavailable until saves exist. Create Development profile and test scaffolding. | Complete: 9 Edit Mode tests and 1 Play Mode integration test pass; visible Editor and Windows Development player flows pass; build and final Console have 0 errors/warnings. No multiplayer package installation. |
| 2 - Player/input/camera | XY-plane movement, independent absolute-pointer/right-stick aim, fixed-orientation follow camera, identified gameplay intentions, Rigidbody2D collision, and Pause/Resume/return input gating in a primitive fixture. | Complete: 20 Edit Mode and 4 Play Mode tests pass; actual Editor controls/focus and WindowsDevelopment standalone smoke pass. Build and final Console: 0 errors/warnings. Gamepad verified with virtual devices; physical hardware unavailable. See Phase 2 report and inherited shutdown diagnostic limitation. |
| 3 - Weapons/basic combat | One Attack command using the independent world-space aim direction, simulation-owned validation/runtime state, static definitions, reusable melee/projectile behavior, health/damage. Validate Knife and Blaster first, then the other approved weapons when behavior/tuning is agreed. | Cooldown/equipment/damage tests through the gameplay command boundary; attacks use the aim direction independently of movement; Play Mode checks against test fixtures, without inventing enemy content. |
| 4 - Deterministic generation | Define the approved graph rules, room categories/IDs, seeded generator, and save-compatible generation data. No prefab spawning in generator logic. | Edit Mode invariants: identical seeds/settings, unique IDs, required Starting room, reachability and valid connections under agreed graph rules. |
| 5 - Room loading/transitions | Reusable room prefabs in a persistent gameplay scene; gameplay-system-owned entry/exit orchestration, placement and clean teardown. | Starting room -> exit -> next room loop; repeated transition/input/lifecycle checks, including invalid content handling. Deterministic generation and basic transitions must be operational before Phase 5A. |
| 5A - Two-player multiplayer technical spike | In a controlled test session, prove join, distinct player spawning into one run, independent movement, attacks, one authoritative procedural seed/run state, and shared room transitions. Evaluate and approve only the minimum networking stack required at this milestone. | Demonstrate all six acceptance checks below in two running game instances. Identify and fix architectural problems, then repeat the checks before Phase 6 or substantial content work. No Steam matchmaking, achievements, progression, public lobbies, or release networking. |
| 6 - Save/Continue/Load | After the spike gate passes, implement versioned DTO, room checkpoint policy, reconstruction, safe disk writes, valid-save enumeration, Continue and Load integration. | Save round trip, equivalent reconstructed layout, corrupt/unsupported save rejection, interrupted-write recovery, fresh-process resume at the agreed checkpoint. |
| 7 - Complete menus/options/death | Finish Main/Pause/Load/Options/Death UI; master volume, resolution and three display modes; separate settings persistence; agreed death actions. | Controller/mouse focus and navigation; display changes in a built player; preferences survive restart; invalid saves never appear playable. |
| 8 - First complete run | Assemble the five weapons and six room categories with only explicitly approved gameplay content and a defined run completion condition. | A full playable run through required menu/room/save/death flows. Gate: objectives, exit conditions, threats, and run endpoint defined by owner. |
| 9 - Hardening/profiling/release | Extend regression coverage, profile representative gameplay, fix measured hotspots, finalize Development/Release profiles and build checks, polish approved content. | Passing project suites, build smoke tests, agreed performance target measured on target hardware, no unexplained project errors. |
| 10 - Production multiplayer/Steam, deferred | Use Phase 5A findings when defining full co-op and platform requirements. Reassess the validated networking stack and Unity Platform Toolkit/Steam support against actual release needs. | Separate production scope/stack approval before expanding SDKs or implementation. The earlier spike does not authorize matchmaking, public lobbies, achievements, progression, or release networking. |

### Phase 5A scope and acceptance gate

Prerequisites: deterministic generation and basic room transitions work, and the existing movement/attack slices use the command boundary. The final 2D/3D and NSEW design decisions are already resolved; their implementation remains part of the preceding phases. Before the spike starts, agree only the technical assumptions needed for the test: authority role/topology, a controlled join method, player ownership/spawn mapping, and a minimal shared-transition trigger/readiness rule. This is not a requirement to settle all production co-op design first.

At that point, inspect available Unity/package versions and evaluate the smallest appropriate stack, including NGO/Unity Transport and Multiplayer Play Mode where useful. Multiplayer Services is not a mandatory dependency merely because it is a candidate for later work. Record the stack decision and obtain approval before installing the required spike packages. No packages are installed or selected by this roadmap revision, and none are added during the initial foundation phases.

The spike must demonstrate in two running game instances that:

1. Both players join the same controlled test session.
2. They receive distinct player identities and spawn into the same run and room.
3. Each moves independently, with commands affecting only the player they control and state resolved by the agreed authority.
4. Both can attack using the existing equipped-weapon command path; the authority validates attacks and resolves gameplay effects, with consistent resulting state visible to both players.
5. One authority supplies the run seed and owns generated run/current-room state; both instances agree on the run, room IDs, and layout instead of creating independent runs.
6. Room transitions are committed by that authority and move both players into the same destination room under the agreed test transition rule.

Keep the spike limited to those proofs, using existing minimal rooms/weapons and test fixtures. Full Steam matchmaking, achievements, progression, public lobbies, production reconnect/host migration, and release networking are out of scope. Do not build larger save/content systems to support the spike.

Record the validation results and architectural findings. If the spike exposes problems with authority, player identity, input coupling, run ownership, or transition orchestration, fix them and repeat the affected checks before proceeding to Phase 6 or substantial game content. Passing this gate validates the architectural direction; it does not certify production networking or approve wider multiplayer scope.

[Unity 6 Build Profiles](https://docs.unity3d.com/6000.3/Documentation/Manual/build-profiles.html) can store independent development/release configurations as version-controlled assets; the cached platform profiles are not a substitute for that deliverable.

Every implementation slice must import/compile, inspect the Console, fix introduced failures, run relevant tests, and verify Play Mode/build behavior where meaningful before adding the next system. Update this document after meaningful changes. Keep profiler-driven optimization in the workflow, with no speculative pooling or ECS conversion.

## Resolved presentation and control decisions

Owner clarification on 2026-08-30:

- Final dimension/rendering: fixed top-down 2D, preserving the existing URP 2D setup. The 2D/3D decision is closed.
- NSEW viewing: a fixed top-down view of the world, not rotation between cardinal camera viewpoints. The NSEW ambiguity is closed.
- Camera: follow/center on the controlled player while maintaining a fixed orientation; movement, facing, and aiming do not rotate the camera.
- Movement: keyboard input moves the player in the normal 2D top-down XY plane, independently of aim.
- Mouse aiming: absolute mouse position determines the player-to-pointer 360-degree world-space aim direction on that plane. Do not use mouse delta as the primary aiming model.
- Gamepad aiming: right-stick direction through the same gameplay aim-intention boundary, implemented in Phase 2.
- Attacks: exactly one Attack command, using the independent aim direction and equipped weapon through Input -> Command -> Simulation -> State -> Presentation.

These are approved requirements. Phase 1 foundation is complete; the current authorization covers their Phase 2 player/input/camera implementation only. No Phase 3 attack/combat behavior is authorized.

## Unresolved design decisions

Remaining platform/control scope:

- Windows Intel 64-bit is approved and validated for Phase 1. Which additional desktop platforms, controller requirements, and performance/resolution targets should later phases support?

Decide before generation, saving, and content milestones:

- Linear room sequence or branching graph? Is backtracking allowed? Is generation selecting reusable rooms, generating their interior geometry, or both? Finite run or endless loop, and what constitutes completion?
- What permits a room exit? What can damage/kill the player, and what minimum combat encounter is approved? No enemy roster or objective system has been specified.
- What are the exact behaviors of Knife, Tree, Blaster, Long Blaster, and Shotgun Blaster? How is equipment selected/changed without assuming loot or inventory mechanics? Is attack pressed once or repeatable while held?
- Does Continue load the checkpoint at room entry or another point? Which room/player state must persist? How many save slots, and what happens to existing saves on New Game, death, and Exit to Main Menu?
- Which actions appear on the death screen? No death penalty or save deletion should be assumed.

Before Phase 5A, agree the limited technical assumptions listed in its scope/acceptance gate. Full co-op rules, matchmaking, and release platform integration remain decisions for Phase 10, not prerequisites for the small spike.

## Known technical debt and validation gaps

- Live scene, Console, active Build Profile, tests, Play Mode flow, and a Development build/standalone smoke test are validated through Phase 2. No direct Unity integration tool is available; project Editor utilities plus native UI control provide the workflow. Physical gamepad testing remains outstanding.
- Investigate whether the logged terrain-shader/D3D12 diagnostics affect the actual chosen renderer and target; do not hide warnings or replace URP based only on startup logs.
- GraphicsSettings lacks a fallback pipeline asset; all existing quality tiers currently provide URP. Revisit intentionally when establishing profiles or changing quality tiers.
- Phase 2 curated Move/Aim/Pause and reserved Attack; UI and its broader device schemes remain intact. Touch/XR/joystick gameplay and physical controller certification are not implemented.
- Application/player tests, the Development profile, and explicit dependency wiring exist. Release identity/configuration remain future work. Normal runtime entry must use Boot; menu/gameplay scenes do not bootstrap themselves.
- The command/state boundary is validated for local navigation, movement, and aim. Multiplayer authority is not validated. Phase 5A must expose and resolve problems before larger save/content work; deterministic generation alone does not prove multiplayer correctness.
- Verify Unity Smart Merge setup across the team. Preserve visible metadata, stable GUIDs, current changes, and locked package versions.

## Significant implementation notes

- Intake added documentation only. It did not change scripts, scenes, prefabs, packages, input bindings, project settings, or build configuration.
- The owner approved the revised roadmap with the early multiplayer technical spike and authority boundary, then authorized Phase 0 live validation only. That validation completed without gameplay code or multiplayer packages. The later Phase 1 authorization and implementation are recorded separately above.
- The owner subsequently resolved fixed top-down URP 2D presentation, fixed-orientation player-follow camera behavior, NSEW meaning, and independent movement/mouse-position aiming. The roadmap records these decisions and removes their former design gates; implementation remains outside the Phase 0 scope.
- Preserve the original weapons, room categories, menu/options/save goals, and other milestones. The inserted Phase 5A validates multiplayer architecture before larger saves/content; Phase 10 remains production multiplayer/Steam work. Follow the explicit presentation/control decisions above; do not infer wider multiplayer scope or unlisted game mechanics from template assets or the spike.
