# Outpost Omega (Unity) · project structure

This is the target layout for the whole project and the rules that keep it tidy. Files marked **now** exist today; everything else is tagged with the roadmap step that creates it (M1.3 = Milestone 1, step 3). When a step lands, its files go where this document says, and the document is updated if the plan changes.

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
- **Numbers live in data files, not code.** Balance stays in `StreamingAssets/Outpost/data/*.json`, shared with the Python game until the balance pass in Milestone 5.
- **Each layer becomes its own assembly** (`.asmdef`) at M1.1, so the compiler enforces the table above and Sim changes do not recompile the UI.

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
   │  ├─ maps/<map id>/            now · 4 maps · M2: all 8 maps x 3 sizes + 4 hand-made
   │  │                                  M3: campaign and Siege Run maps
   │  ├─ models.pak.json / .bin    now · every unit, building and turret model
   │  └─ audio.json                M4 · which sound plays for which event
   └─ Outpost/
      ├─ Scenes/
      │  ├─ Boot.unity             M2 · first scene: loads settings and saves, opens the menu
      │  ├─ MainMenu.unity         M2
      │  ├─ Battle.unity           now as Skirmish.unity · renamed at M2, used by every mode
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
      ├─ Art/                      materials, shaders, effects, portraits, icons, fonts
      ├─ Audio/                    sound effects, music, voice
      └─ UI/                       layout and style files for the interface
```

## Scripts in detail

### Content (layer 1)

```
Content/
├─ MapBuilder.cs        now · loads a map folder, builds the terrain mesh, Ground(x, y)
├─ MapData.cs           M1.1 · the map data classes, split out of MapBuilder.cs
├─ ModelLibrary.cs      now · loads the model pak, spawns a model by name
├─ PortraitLibrary.cs   M1.1 · unit and building portraits for the HUD (rendered once from the models)
├─ MapGenerator.cs      M4 · the Python map generator rebuilt in C# (needed by the Map Builder)
└─ TerrainPainter.cs    M4 · bakes the painted ground for generated and user-made maps
```

### Sim (layer 2)

`Battle.cs` is one 1,400-line file today. At M1.3, before map control is added, it is split into partial files by system. Nothing about how it works changes; it only becomes possible to find things.

```
Sim/
├─ Core/
│  ├─ Battle.cs               now · the battle: lists of everything, Tick, setup, victory
│  ├─ BattleConfig.cs         M2 · everything that defines one battle: map, size, difficulty, leader,
│  │                                loadout, mode rules, strains. The only way a battle is created
│  ├─ IBattleFx.cs            M1.3 · split out of Battle.cs
│  ├─ GameData.cs             now · reads the JSON data
│  ├─ NavGrid.cs              now · walkable tiles and pathfinding
│  ├─ Entity.cs, Unit.cs, Building.cs, Tower.cs, Base.cs, Faction.cs
│  │                          now as Entities.cs · split one class per file at M1.3
│  └─ Projectiles.cs          M1.3 · Projectile, Strike, Zone, ScanArea (from Entities.cs)
├─ Systems/                   partial pieces of Battle, one per rules area
│  ├─ Battle.Construction.cs  now inside Battle.cs · bases, buildings, upgrades, research
│  ├─ Battle.Production.cs    now inside Battle.cs · unit queues, spawning, hero respawn
│  ├─ Battle.Orders.cs        now inside Battle.cs · move, attack, stop, formations
│  ├─ Battle.Units.cs         now inside Battle.cs · per-unit update: targeting, movement, firing
│  ├─ Battle.Combat.cs        now inside Battle.cs · Hit, Splash, projectiles, strikes, zones
│  ├─ Battle.Abilities.cs     now inside Battle.cs · unit abilities and buffs
│  ├─ Battle.Support.cs       now inside Battle.cs · healers, shields
│  ├─ Battle.Detection.cs     now inside Battle.cs · stealth, detection, Scan
│  ├─ Battle.Economy.cs       now inside Battle.cs · income, pop cap, tech
│  ├─ Battle.MapControl.cs    M1.3 · expansions, wild nests, relays, crates, recycling
│  ├─ Battle.Towers.cs        M1.4 · tower update, tiers, upgrade and sell
│  ├─ TowerBehaviors.cs       M1.4 · the behavior types that cover all 34 towers
│  ├─ Battle.Bunkers.cs       M1.4 · bunkers and garrisons
│  ├─ Battle.Powers.cs        M1.6 · leader powers and alien powers
│  ├─ Leaders.cs              M1.6 · the 9 leaders' passives
│  └─ Fog.cs                  M1.7 · fog of war and what each side has seen
├─ AI/
│  ├─ AlienAI.cs              now · the Swarm's commander
│  ├─ AlienAI.Economy.cs      M1.3 · split out, plus expansion plans
│  ├─ AlienAI.Army.cs         M1.3 · split out, plus relay fights; M1.7 scouting and raids
│  ├─ AlienAI.Powers.cs       M1.6 · when it casts Acid Rain, Spore Storm, Swarm Call
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
├─ BattleView.cs         M1.1 · owns the picture of one battle (replaces the view half of SkirmishBootstrap)
├─ EntityView.cs         now · one unit, building or tower on screen
├─ UnitAnimator.cs       M1.2 · code-driven animation: legs, treads, turrets, rotors, attack, death
├─ AnimationRigs.cs      M1.2 · which parts of each model move, and how
├─ BaseApron.cs          now · the connected base platform
├─ RtsCamera.cs          now
├─ Effects/
│  ├─ Fx.cs              now · the IBattleFx receiver; hands work to the files below
│  ├─ WeaponEffects.cs   M1.5 · muzzle flashes, tracers, projectiles, explosions
│  ├─ AbilityEffects.cs  M1.5 · one proper visual per ability and power
│  └─ EffectPool.cs      M1.5 · recycles effect objects
├─ World/
│  ├─ Lighting.cs        M1.8 · sun, shadows, glowing structures, color grade per biome
│  ├─ FogView.cs         M1.7 · draws the fog of war
│  ├─ Markers.cs         M1.1 · selection rings, rally and order markers, health bars in the world
│  ├─ Creep.cs           M4 · creep spreading around alien bases
│  ├─ Decals.cs          M4 · craters, scorch marks, tracks
│  └─ Weather.cs         M4
└─ Audio/
   ├─ BattleAudio.cs     M4 · plays sounds for IBattleFx events
   └─ Music.cs           M4

Input/
├─ CommandInput.cs       now in View/ · selection, orders, aim modes · moves here at M1.1
├─ Hotkeys.cs            M1.1 · every key in one table · M2: rebinding
└─ SecondPlayer.cs       M4 · the co-op Commander (keys or controller)
```

### UI (layer 4)

Built with Unity's UI Toolkit: a layout file (`.uxml`) and a style file (`.uss`) per screen in `Assets/Outpost/UI/`, and one small C# class per screen here. The placeholder `View/Hud.cs` is deleted when `Hud/` replaces it at M1.1.

```
UI/
├─ Hud/                       M1.1
│  ├─ HudRoot.cs              builds the HUD and tells each panel which battle to show
│  ├─ ResourceBar.cs          supplies, income, pop, tech, clock, next attack
│  ├─ SelectionPanel.cs       portraits, health, squad, stats, buffs
│  ├─ CommandCard.cs          the button grid: orders, abilities, build, train, research, upgrades
│  ├─ Minimap.cs              M1.7 adds fog and alert pings
│  ├─ Alerts.cs               messages and "last alert" jump
│  ├─ PowerBar.cs             M1.6 · Z X C V leader powers
│  ├─ ObjectivesPanel.cs      M3 · campaign objectives
│  └─ BossBar.cs              M3 · Siege Run boss health and phases
├─ Menus/                     M2
│  ├─ MainMenu.cs
│  ├─ SkirmishSetup.cs        map, size, difficulty, leader, tower loadout
│  ├─ CommandersScreen.cs     the roster with its four tabs
│  ├─ ResultsScreen.cs        win or lose, stats, per-map records
│  ├─ PauseMenu.cs
│  ├─ SettingsScreen.cs       video, audio, controls
│  └─ HelpOverlay.cs          controls and first-match hints
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
│  └─ MapBuilderScreen.cs
└─ Shared/                    M1.1
   ├─ Tooltip.cs, IconButton.cs, ProgressBar.cs
   └─ Format.cs               one place for how numbers, times and costs are written
```

### Game (layer 5)

```
Game/
├─ SkirmishBootstrap.cs   now · builds a hard-coded battle · replaced at M2 by the three files below
├─ GameFlow.cs            M2 · moves between Boot, the menu and a battle; knows which mode is running
├─ BattleSession.cs       M2 · takes a BattleConfig, creates the Battle, BattleView and HUD, runs the clock
├─ ModeRunner.cs          M3 · attaches the right director (Mission, SiegeRun node, LastStand...) to a session
├─ SaveData.cs            M2 · the save file: records, unlocks, Commander, loadout, campaign, run in progress
├─ Settings.cs            M2 · video, audio, key bindings
└─ MapBuilder/            M4 · the in-game map editor's tools (paint, place, mirror, save, test)
```

Saves and user-made maps are written to the player's own data folder (`Application.persistentDataPath`), never into the game folder: `save.json`, `settings.json`, `maps/<name>/`.

### Editor

```
Editor/
├─ OutpostMenu.cs       now · the Outpost menu
├─ HeadlessMatch.cs     now · full matches without graphics, for checking rules changes
├─ ParityCheck.cs       M5 · runs the same scripted fights as the Python game and compares
├─ ContentImport.cs     M2 · one click to re-run the Python export for data, maps or models
└─ BuildTools.cs        M5 · makes the Windows build
```

## Non-code assets

```
Assets/Outpost/
├─ Art/
│  ├─ Materials/     M1.2 · shared materials for the models (today they are made in code)
│  ├─ Shaders/       M1.8 · team color and glow · M4 creep and decals
│  ├─ Effects/       M1.5 · particle systems and textures
│  ├─ Portraits/     M1.1 · generated unit and building portraits
│  └─ Icons/         M1.1 · ability, order, research and resource icons
├─ Audio/            M4
│  ├─ Sfx/, Music/, Voice/
└─ UI/
   ├─ Layouts/       M1.1 · .uxml, one per screen or panel
   ├─ Styles/        M1.1 · .uss · Theme.uss holds every color, size and font in one place
   └─ Fonts/         M1.1
```

## Where the content comes from

Models, maps and data are still produced by the Python project (`tools/export_unity.py` in `Outpost-Omega-Updated`) and written into `StreamingAssets/Outpost/`. That stays true until M4, when the map generator is rebuilt in Unity; the sculpting tools for models stay in Python for good.

## How one battle fits together

1. `GameFlow` is told to start something (a Skirmish, Operation 3, a Siege Run node) and builds a `BattleConfig`.
2. `BattleSession` loads the map through `Content`, creates the `Battle` (Sim), then a `BattleView` and the `HudRoot`, and hands the mode's director to `ModeRunner`.
3. Every frame `BattleSession` ticks the `Battle`. The Sim reports events through `IBattleFx`; `Fx`, `BattleAudio` and the HUD react.
4. `CommandInput` and the HUD turn clicks and keys into `Battle` method calls.
5. When the `Battle` or the director declares a result, `GameFlow` saves it and shows the results screen.

The headless test tool does steps 1 to 3 with no view at all, which is why the Sim must never reach upward.

## Still open

- **UI Toolkit versus the older Unity UI.** This document assumes UI Toolkit. If the first HUD panels fight it, the folders stay the same and only the layout files change format.
- **Classic Defense.** Not laid out here. If it is ported it gets its own `Sim/Classic/`, `View/Classic/` and `UI/Classic/` beside the RTS, sharing Content, Game, Shared UI and the Art and Audio folders.
- **Animation.** If code-driven animation is not enough (decided at M1.2), `Art/` gains `Models/` and `Animations/` for rigged characters and `UnitAnimator.cs` drives those instead.
