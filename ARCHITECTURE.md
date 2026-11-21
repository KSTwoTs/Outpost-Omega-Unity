# Outpost Omega (Unity) · project structure

This is the target layout for the whole project and the rules that keep it tidy. Files marked **now** exist today; everything else is tagged with the roadmap step that creates it (M1.3 = Milestone 1, step 3). When a step lands, its files go where this document says, and the document is updated if the plan changes.

**Placeholders (Nov 18).** Every planned script below already exists as an empty placeholder: a header comment saying what it will hold and which step fills it, and an empty namespace, so it compiles to nothing. A step fills its placeholder in rather than creating the file. The housekeeping pass (Nov 19) moved the code out of `Battle.cs`, `Entities.cs`, `AlienAI.cs` and `MapBuilder.cs` into the matching placeholders and moved the remaining files into `Sim/Core/` and `Sim/AI/`. Empty asset folders hold a `.gitkeep` so git keeps them. The scenes `Boot.unity` and `MainMenu.unity` were made and `Skirmish.unity` was renamed to `Battle.unity` (GUID kept) in M2 group 1 (Nov 21); `SkirmishBootstrap.cs` was removed then (BattleSession + BattleView replace it). Not created yet: `audio.json` (M4), and anything for Classic Defense (undecided). Rigged animation was decided on Nov 20 (M5): `Art/Models/` and `Art/Animations/` exist with a `.gitkeep`. Placeholders in `Input/` use the `Outpost.View` namespace like `CommandInput` does, because a namespace called `Outpost.Input` would hide Unity's `Input` class inside the project.

**Milestones (Nov 20).** Six milestones: M1 Skirmish, M2 front end, M3 map generator plus campaign and Siege Run systems (Siege Run battles on generated maps), M4 sound, performance and smaller modes, M5 art, maps and levels (final art and animation, reactive world, hand-made maps, Map Builder on the M3 generator, campaign levels), M6 balance and release. Until M5 the game keeps today's models, the four Medium maps and placeholder looks.

## The five layers

Code only ever depends downward in this list. That one rule is what lets the battle be tested without graphics and lets every mode reuse the same battle.

| Layer | Folder | What it is | May use |
| --- | --- | --- | --- |
| 5. Game | `Scripts/Game` | What happens outside and around a battle: menus flow, which mode is running, saves, settings | everything below |
| 4. UI | `Scripts/UI` | Everything drawn in screen space: HUD, menus | View, Input, Sim (read only) |
| 3. View and Input | `Scripts/View`, `Scripts/Input` | The 3D picture of a battle and the player's mouse and keyboard | Sim, Content |
| 2. Sim | `Scripts/Sim` | The battle rules, the AI, missions and runs. Pure C#: no GameObjects, no scenes | Content data classes only |
| 1. Content | `Scripts/Content` | Loading maps, models and data files | nothing |

Rules that follow from it:

- **The Sim never knows it is being watched.** It talks to the outside only through `IBattleFx` (things happened: a shot, a death, a message). Views and the HUD read Sim objects but change them only by calling `Battle` methods (`OrderMove`, `QueueUnit`, `UseAbility`...).
- **A mode is a configuration plus an optional director, not a new battle.** Skirmish, a campaign operation, a Siege Run node and Last Stand all create the same `Battle` from a `BattleConfig`; the campaign adds a `Mission`, Siege Run adds strains and a `BossDirector`.
- **Numbers live in data files, not code.** Balance stays in `StreamingAssets/Outpost/data/*.json`, shared with the Python game until the balance pass in Milestone 6.
- **Each layer is its own assembly** (`.asmdef`, since the housekeeping pass on Nov 19), so the compiler enforces the table above and Sim changes do not recompile the UI: `Outpost.Content` (Scripts/Content), `Outpost.Sim` (Scripts/Sim, references Content), `Outpost.View` (Scripts/View, plus Scripts/Input through `Input/Outpost.View.asmref`; references Content, Sim, Input System, URP), `Outpost.UI` (Content, Sim, View, Input System), `Outpost.Game` (everything below, Input System, URP), `Outpost.Sandbox` (Content, Sim, View, URP) and `Outpost.Editor` (Editor/, Editor platform only, references all of them). Newtonsoft JSON needs no entry: its package DLL is auto-referenced. A new folder of scripts inside a layer joins that layer's assembly automatically; a new layer or a new upward use needs the asmdef changed (and should not happen).

## Folder tree

```
Outpost-Omega-Unity/
├─ ARCHITECTURE.md                 this file
├─ README.md                       now · how to open and run the project
├─ .gitignore                      now
├─ Packages/                       now · manifest (Newtonsoft JSON, MCP for Unity, URP, Input System)
├─ ProjectSettings/                now
└─ Assets/
   ├─ Settings/                    now · URP render pipeline assets
   ├─ StreamingAssets/Outpost/     shipped game content, read at run time
   │  ├─ data/                     now · rts.json, towers.json, commanders.json, campaign.json,
   │  │                                  rts_run.json, story.json ... (copied from the Python game)
   │  ├─ maps/<map id>/            now · 4 maps exported from Python · M3: generated in Unity instead · M5: the 4 hand-made maps
   │  │                                  M5: campaign levels and Siege Run boss arenas
   │  ├─ models.pak.json / .bin    now · every unit, building and turret as one solid mesh
│  ├─ models_anim.pak.json/.bin now · units and turrets again, with walk and attack poses and
│  │                                  separate turrets, rotors and turret heads (these win over models.pak)
   │  └─ audio.json                M4 · which sound plays for which event
   └─ Outpost/
      ├─ Scenes/
      │  ├─ Boot.unity             now · first scene (GameFlow object): loads settings and saves (group 3), opens the menu
      │  ├─ MainMenu.unity         now · camera + light only; GameFlow draws the menu screens here (played directly, GameFlow makes itself)
      │  ├─ Battle.unity           now · renamed from Skirmish.unity at M2 (GUID kept), BattleSession object; used by every mode;
      │  │                         played directly it starts a default Skirmish from the BattleSession inspector fields
      │  └─ Sandbox.unity          now · model look test
      ├─ Scripts/
      │  ├─ Content/               (layer 1)
      │  ├─ Sim/                   (layer 2)
      │  ├─ View/                  (layer 3)
      │  ├─ Input/                 (layer 3)
      │  ├─ UI/                    (layer 4)
      │  ├─ Game/                  (layer 5)
      │  └─ Sandbox/               now · SandboxBootstrap.cs
      ├─ Editor/                   editor-only tools, never in a build
      ├─ Resources/UI/             now · the interface's style sheet and theme
      ├─ Resources/Effects/        now · the two effect base materials (keep the effect shaders in builds)
      ├─ Art/                      materials, shaders, effects, icons
      └─ Audio/                    sound effects, music, voice
```

## Scripts in detail

### Content (layer 1)

```
Content/
├─ MapBuilder.cs        now · loads a map folder, builds the terrain mesh, Ground(x, y)
├─ MapData.cs           now · the map data classes (MapData, MapData.SiteData), split out of MapBuilder.cs
├─ ModelLibrary.cs      now · loads the model pak, spawns a model by name
├─ PortraitLibrary.cs   now · unit and building portraits for the HUD (photographed once from the models)
├─ MapGenerator.cs      M3 · the Python map generator (rts/world.py) rebuilt in C#: seed + theme + size -> map, with rules for what can form; used by Siege Run, Skirmish and the Map Builder
└─ TerrainPainter.cs    M3 · bakes the painted ground for generated and user-made maps
```

### Sim (layer 2)

`Battle` is one `partial` class spread over `Core/Battle.cs` and one file per rules area in `Systems/` (split in the housekeeping pass on Nov 19, code moved verbatim). `Battle.cs` keeps the lists, the constructor, `Tick`, victory and the spatial queries (`Near`, `Acquire`). `AlienAI` is split the same way.

```
Sim/
├─ Core/
│  ├─ Battle.cs               now · the battle: lists of everything, Tick, setup, victory
│  ├─ BattleConfig.cs         now · everything that defines one battle: map, size, difficulty, leader,
│  │                                loadout, seed (M3: mode rules, strains). Create(map, fx, beforeStart) is how the game makes a battle
│  │                                (rules tests may still call new Battle(map)); also BattleResult (the results screen's numbers)
│  ├─ IBattleFx.cs            now · split out of Battle.cs; M1.5 added TowerFired, Ability and Float, M1.6 Power (notifications only)
│  ├─ GameData.cs             now · reads the JSON data
│  ├─ NavGrid.cs              now · walkable tiles and pathfinding
│  ├─ Entity.cs, Unit.cs, Building.cs, Tower.cs, Base.cs, Faction.cs
│  │                          now · one class per file (Unit.cs also holds Order and Dash, Building.cs
│  │                                QueueItem, Base.cs the map-control Relay and Crate)
│  └─ Projectiles.cs          now · Projectile, Strike, Zone, ScanArea (from Entities.cs)
├─ Systems/                   partial pieces of Battle, one per rules area
│  ├─ Battle.Construction.cs  now · bases, buildings, turret placement, costs, upgrades, research
│  ├─ Battle.Production.cs    now · unit queues, spawning and unit multipliers (UnitMods), hero spawn, queue completion
│  ├─ Battle.Orders.cs        now · move, attack, stop, formations
│  ├─ Battle.Units.cs         now · per-unit update: targeting, movement, firing
│  ├─ Battle.Combat.cs        now · Hit, Splash, Kill and veterancy, projectiles, strikes, zones
│  ├─ Battle.Abilities.cs     now · unit abilities and buffs
│  ├─ Battle.Support.cs       now · healers, shields
│  ├─ Battle.Detection.cs     now · stealth, detection, Scan
│  ├─ Battle.Economy.cs       now · income, hero respawn timer, pop cap, tech
│  ├─ Battle.MapControl.cs    now · expansions, wild nests, relays, crates, recycling
│  ├─ Battle.Towers.cs        now · the tower update · M1.4 adds the full loadout, tiers, upgrade and sell
│  ├─ TowerBehaviors.cs       now · the behavior types that cover all 34 towers; M2: loadout rules (PerCategory, TowersIn,
│  │                                CleanLoadout, ToggleLoadout). Battle.BuildTower refuses towers outside Faction.loadout
│  ├─ Battle.Bunkers.cs       M1.4 · bunkers and garrisons
│  ├─ Battle.Powers.cs        now · leader powers (Z X C + the leader's V) and alien powers; cooldowns tick in Economy
│  ├─ Leaders.cs              now · the 9 leaders' passives (SetLeader, costs, structure HP) · picked on SkirmishSetup / CommandersScreen since M2
│  └─ Fog.cs                  now · fog of war per side, what each side has seen (Seen), the positioned alert log
├─ AI/
│  ├─ AlienAI.cs              now · the Swarm's commander
│  ├─ AlienAI.Economy.cs      now · shopping list and savings, expansions, unit production, evolutions
│  ├─ AlienAI.Army.cs         now · defense, relay raiders, expansion escorts, attack waves, the Queen, scouting and raids
│  ├─ AlienAI.Powers.cs       now · when it casts Acid Rain, Spore Storm, Swarm Call
│  └─ AllyAI.cs               M4 · the allied human commander for 2v2
├─ Missions/                  M3 · the campaign
│  ├─ Mission.cs              runs one operation from campaign.json
│  ├─ Objectives.cs           objective types (build, survive, hold, rescue, escort, destroy...)
│  ├─ Triggers.cs             timed lines, first sightings, scripted waves and ambushes
│  └─ CampaignProgress.cs     which operations are done, unlocks per operation
├─ Run/                       M3 · Siege Run
│  ├─ SiegeRun.cs             the run: acts, node map, Command Integrity, Salvage
│  ├─ Strains.cs              stacking strains and adaptation
│  ├─ Cards.cs                augments, unlocks, blueprints, recruits, relics
│  ├─ Veterans.cs             units carried between battles
│  ├─ BossDirector.cs         the three boss wars: gates, phases, the Seed Heart
│  └─ TechLab.cs              permanent upgrades bought with Salvage
└─ Modes/                     M4 · rules for the smaller modes
   ├─ LastStand.cs, BossRush.cs, DailyCommand.cs, Sandbox.cs
   └─ Mutators.cs             Hardcore and Mutated toggles
```

### View and Input (layer 3)

```
View/
├─ BattleView.cs         now · owns the picture of one battle: camera rig, World (terrain, lighting, Fx, fog, props), input, markers
├─ EntityView.cs         now · one unit, building or tower on screen: stepping, attack pose, lean, banking,
│                              turret tracking, falling over on death
├─ UnitAnimator.cs       now · ModelRig: swaps a model between its exported poses, aims turret parts, spins rotors
│                              (placeholder: replaced at M5 by Animator-driven rigged characters)
│                              (which parts move comes from the model pack's index, so there is no rig file)
├─ BaseApron.cs          now · the connected base platform
├─ RtsCamera.cs          now · also camera shake (Shake / ShakeAt) since M1.5; keys from Hotkeys, + / - zoom and gamepad sticks since M2
├─ Effects/
│  ├─ Fx.cs              now · the IBattleFx receiver; hands work to the files below (moved here from View/ at M1.5)
│  ├─ WeaponEffects.cs   now · muzzle flashes, tracers, projectiles, explosions, beams, lightning, patches, deaths,
│  │                           the looks of all 34 tower behaviours; FxColors (the Python palette)
│  ├─ AbilityEffects.cs  now · one visual per unit ability, Scan, generic rings; Power() draws simple first looks for every power
│  └─ EffectPool.cs      now · recycles every effect object (quads, lines, 4 shared particle systems), capped
├─ World/
│  ├─ Lighting.cs        now · sun, shadows, ambient, haze, post Volume (tonemap, grade, bloom, vignette) and lava/acid glow per map theme (Mood)
│  ├─ FogView.cs         now · draws the fog of war (sheet over the ground, one texel per tile)
│  ├─ Markers.cs         now · health, shield and progress bars over units, rank stars, relay capture bars, the drag box
│  │                          (Suppressed while the HUD shows pause, settings or controls; M2 group 4)
│  ├─ MapProps.cs        now · base site pads, base aprons, relay towers (by owner), supply crates
│  ├─ Creep.cs           M5 · creep spreading around alien bases
│  ├─ Decals.cs          M5 · craters, scorch marks, tracks
│  └─ Weather.cs         M5
└─ Audio/
   ├─ BattleAudio.cs     M4 · plays sounds for IBattleFx events
   └─ Music.cs           M4

Input/
├─ CommandInput.cs       now · selection, orders, aim modes; Handle(id) runs every battle key action (moved here from View/
│                              at M2 with AssetDatabase.MoveAsset, GUID and namespace Outpost.View kept); game speed, base / alert jumps
├─ Hotkeys.cs            now · every key in one table (65 actions: id, context battle/menu, default keys, description, pad button),
│                              Pressed / Held / Labels, Rebind (the other action loses the key), ToJson / FromJson,
│                              LoadHook / SaveHook (set by Game/Settings.cs: settings.json "controls"), basic gamepad (sticks, A / B / Start / Select / D-pad)
└─ SecondPlayer.cs       M4 · the co-op Commander (keys or controller)
```

### UI (layer 4)

Built with Unity's UI Toolkit. Each panel is laid out in C#, one small class per panel, and every color and size lives in one style file, `Assets/Outpost/Resources/UI/Outpost.uss`. The placeholder `View/Hud.cs` was removed when `Hud/` replaced it on Nov 12.

```
UI/
├─ Hud/                       now
│  ├─ HudRoot.cs              now · builds the HUD, tells each panel which battle to show, help, end screen; hosts the
│  │                               PauseMenu and the SettingsScreen opened from it (M2 group 3)
│  ├─ ResourceBar.cs          now · supplies, income, pop, tech, clock, next attack, Commander respawn
│  ├─ SelectionPanel.cs       now · portrait, health, shield, stats, buffs, the build queue; a tile per type for groups
│  ├─ CommandCard.cs          now · the 5 x 3 button grid: orders, abilities, build, turrets, train, research, upgrades, Scan;
│  │                                each button shows its key; grid hotkeys Alt + Q-T / A-G / Z-B (Hotkeys card_1-15, Press(i)) since M2
│  ├─ MinimapPanel.cs         now · with fog and alert pings
│  ├─ AlertsPanel.cs          now · messages, aim-mode banner, toast (the last-alert jump is CommandInput.JumpAlert, Space, since M2)
│  ├─ PowerBar.cs             now · Z X C V leader powers (top centre; aiming goes through CommandInput.powerAim)
│  ├─ ObjectivesPanel.cs      M3 · campaign objectives
│  └─ BossBar.cs              M3 · Siege Run boss health and phases
├─ Menus/                     M2 (built in C# like the HUD, shown by GameFlow on one menu document)
│  ├─ MainMenu.cs             now · the mode list (modes not built yet are dimmed)
│  ├─ SkirmishSetup.cs        now · map (with preview), size, difficulty, leader card, tower loadout (3 per category)
│  ├─ CommandersScreen.cs     now · the roster with its four tabs; PassiveLines / PowerRows / ClassicLines text from data
│  ├─ ResultsScreen.cs        now · win, lose or left, stats; per-map record line from GameFlow.RecordResult (group 3)
│  ├─ PauseMenu.cs            now · RESUME, SETTINGS, RESTART, QUIT TO MENU (shown by HudRoot; M2 group 3)
│  ├─ SettingsScreen.cs       now · tabs VIDEO / AUDIO / CONTROLS (key rebinding) / GAMEPLAY, APPLY / REVERT / BACK;
│  │                               edits an ISettingsModel (Game/Settings.Model) so UI never reaches into Game
│  └─ HelpOverlay.cs          now · HelpOverlay (F1 controls overlay built from the Hotkeys table), Tips (tooltip text
│                                    with real numbers: units + counters, buildings, turrets per tier, research) and
│                                    FirstMatchHints (first Skirmish, shown once, save.json hints_seen; M2 group 4)
├─ Campaign/                  M3
│  ├─ CampaignScreen.cs       the ten operations and their progress
│  └─ BriefingScreen.cs       briefings, debriefs, radio lines
├─ Run/                       M3
│  ├─ RunMapScreen.cs         the node map
│  ├─ CardDraftScreen.cs      rewards after a win
│  ├─ DepotScreen.cs          depots, camps, the Strain Lab, events
│  └─ TechLabScreen.cs
├─ Modes/                     M4
│  ├─ CommandModesScreen.cs
│  └─ MapBuilderScreen.cs   M5
└─ Shared/
   └─ Ui.cs                   now · element helpers (boxes, text, buttons, bars, portraits; M2: Document, TextButton,
                                    Heading, Para, Bullets; M2 group 3: UI scale Track / SetScale) and how
                                    numbers and times are written
```

### Game (layer 5)

```
Game/
├─ GameFlow.cs            now · moves between Boot, the menu and a battle; knows which mode is running; owns the menu
│                               screens, the last setup, the Commander pick, the last result; loads / writes the save and settings
├─ BattleSession.cs       now · takes a BattleConfig (Pending), creates the Battle, BattleView and HUD, runs the clock,
│                               hands a BattleResult to GameFlow (Finished)
├─ ModeRunner.cs          M3 · attaches the right director (Mission, SiegeRun node, LastStand...) to a session
├─ SaveData.cs            now · save.json (persistentDataPath, Python key names): records, unlocks, Commander, last
│                               setup + loadout, campaign / run placeholders; safe write (.tmp -> replace, .bak), SafeJson
├─ Settings.cs            now · settings.json: video, audio, key bindings (Hotkeys.LoadHook / SaveHook), gameplay;
│                               Apply(), and Model() = what SettingsScreen edits
└─ MapBuilder/            M5 · the in-game map editor's tools (paint, place, mirror, save, test)
```

Saves and user-made maps are written to the player's own data folder (`Application.persistentDataPath`), never into the game folder: `save.json`, `settings.json`, `maps/<name>/`.

### Editor

```
Editor/
├─ OutpostMenu.cs       now · the Outpost menu; Effects Test (edit-mode stills of every effect, pool stress test)
├─ HeadlessMatch.cs     now · full matches without graphics, for checking rules changes; scripted players
│                               efficient / steady / casual / idle and BalanceTable() for the difficulty check (M1.9)
├─ ParityCheck.cs       M6 · runs the same scripted fights as the Python game and compares
├─ ContentImport.cs     now · Outpost > Content: Export Data (C# copy of data/*.json), Export Models (runs
│                              tools/export_unity.py with the Python .venv); maps not offered (M2 group 4)
└─ BuildTools.cs        M6 · makes the Windows build
```

## Non-code assets

```
Assets/Outpost/
├─ Art/
│  ├─ Materials/     M5 · shared materials for the models (today they are made in code)
│  ├─ Models/        M5 · rigged characters (skeleton + skinned mesh), units first, then turrets and buildings
│  ├─ Animations/    M5 · animation clips and Animator controllers (idle, walk, run, attack, hit, death)
│  ├─ Shaders/       now · FxAdditive / FxAlpha (effects) · M5 · team color and glow, creep and decals
│  ├─ Effects/       M1.5 · empty on purpose: effect textures are drawn in code (EffectPool.MakeTexture);
│  │                        hand-made textures or particle prefabs go here if the look needs them later
│  └─ Icons/         M2 · ability, order, research and resource icons (portraits are made at run time)
├─ Audio/            M4
│  ├─ Sfx/, Music/, Voice/
├─ Resources/Effects/ loaded by name at run time: fx_additive.mat, fx_alpha.mat
└─ Resources/UI/     loaded by name at run time
   ├─ Outpost.uss        now · every color, size and font of the interface
   ├─ OutpostTheme.tss   now · Unity's default runtime theme, required by UI Toolkit
   └─ Fonts/             M2
```

## Where the content comes from

Models, maps and data are produced from the Python project and written into `StreamingAssets/Outpost/`. Maps stop coming from Python at M3, when the map generator is rebuilt in Unity; the sculpting tools for models stay in Python for good.

**The export script.** `tools/export_unity.py` in the Python project was rewritten on Nov 12 (the original was never saved there). `models` turns the sculpts into meshes: every unit in up to three poses (standing, mid-step, attacking), with vehicle turrets, rotors and the heads of base turrets exported as separate parts around their own pivots. `data` copies the data files. `maps` is not rewritten yet; it is no longer needed once the generator exists (M3); the 4 hand-made maps are brought over at M5. Buildings can be re-exported with `models buildings` but still come from the original pack.

## How one battle fits together

1. `GameFlow` is told to start something (a Skirmish, Operation 3, a Siege Run node) and builds a `BattleConfig`.
2. `BattleSession` loads the map through `Content`, creates the `Battle` (Sim), then a `BattleView` and the `HudRoot`, and hands the mode's director to `ModeRunner`.
3. Every frame `BattleSession` ticks the `Battle`. The Sim reports events through `IBattleFx`; `Fx`, `BattleAudio` and the HUD react.
4. `CommandInput` and the HUD turn clicks and keys into `Battle` method calls.
5. When the `Battle` or the director declares a result, `GameFlow` saves it and shows the results screen.

The headless test tool does steps 1 to 3 with no view at all, which is why the Sim must never reach upward.

## Still open

- **UI Toolkit versus the older Unity UI.** Settled Nov 12: the HUD is built on UI Toolkit and works. Still to confirm by hand: real mouse clicks on the HUD (the automated test could only send simulated ones).
- **Classic Defense.** Not laid out here. If it is ported it gets its own `Sim/Classic/`, `View/Classic/` and `UI/Classic/` beside the RTS, sharing Content, Game, Shared UI and the Art and Audio folders.
- **Animation.** Decided Nov 20: rigged, animated characters at M5. Until then units keep the code-driven placeholder (exported poses plus movement in code). At M5 `Art/Models/` and `Art/Animations/` fill up and `UnitAnimator.cs` drives Animators instead. The sculpted Python meshes have no skeleton, so the rigged models will likely be rebuilt or retopologised in a 3D tool such as Blender; how is decided when M5 starts.
