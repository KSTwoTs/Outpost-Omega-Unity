# Outpost Omega (Unity) · project structure

This is the target layout for the whole project and the rules that keep it tidy. Files marked **now** exist today; everything else is tagged with the roadmap step that creates it (M1.3 = Milestone 1, step 3). When a step lands, its files go where this document says, and the document is updated if the plan changes.

**Placeholders (Nov 18).** Every planned script below already exists as an empty placeholder: a header comment saying what it will hold and which step fills it, and an empty namespace, so it compiles to nothing. A step fills its placeholder in rather than creating the file. The housekeeping pass (Nov 19) moved the code out of `Battle.cs`, `Entities.cs`, `AlienAI.cs` and `MapBuilder.cs` into the matching placeholders and moved the remaining files into `Sim/Core/` and `Sim/AI/`. Empty asset folders hold a `.gitkeep` so git keeps them. Not created yet: the scenes `Boot.unity` and `MainMenu.unity` (made in the editor at M2), `Battle.unity` (a rename of Skirmish.unity), `audio.json` (M4), and anything for Classic Defense or rigged animation (both undecided). Placeholders in `Input/` use the `Outpost.View` namespace like `CommandInput` does, because a namespace called `Outpost.Input` would hide Unity's `Input` class inside the project.

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
   │  ├─ maps/<map id>/            now · 4 maps · M2: all 8 maps x 3 sizes + 4 hand-made
   │  │                                  M3: campaign and Siege Run maps
   │  ├─ models.pak.json / .bin    now · every unit, building and turret as one solid mesh
│  ├─ models_anim.pak.json/.bin now · units and turrets again, with walk and attack poses and
│  │                                  separate turrets, rotors and turret heads (these win over models.pak)
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
├─ MapGenerator.cs      M4 · the Python map generator rebuilt in C# (needed by the Map Builder)
└─ TerrainPainter.cs    M4 · bakes the painted ground for generated and user-made maps
```

### Sim (layer 2)

`Battle` is one `partial` class spread over `Core/Battle.cs` and one file per rules area in `Systems/` (split in the housekeeping pass on Nov 19, code moved verbatim). `Battle.cs` keeps the lists, the constructor, `Tick`, victory and the spatial queries (`Near`, `Acquire`). `AlienAI` is split the same way.

```
Sim/
├─ Core/
│  ├─ Battle.cs               now · the battle: lists of everything, Tick, setup, victory
│  ├─ BattleConfig.cs         M2 · everything that defines one battle: map, size, difficulty, leader,
│  │                                loadout, mode rules, strains. The only way a battle is created
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
│  ├─ TowerBehaviors.cs       M1.4 · the behavior types that cover all 34 towers
│  ├─ Battle.Bunkers.cs       M1.4 · bunkers and garrisons
│  ├─ Battle.Powers.cs        now · leader powers (Z X C + the leader's V) and alien powers; cooldowns tick in Economy
│  ├─ Leaders.cs              now · the 9 leaders' passives (SetLeader, costs, structure HP) · M2: picked on SkirmishSetup
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
├─ BattleView.cs         M2 · owns the picture of one battle (replaces the view half of SkirmishBootstrap)
├─ EntityView.cs         now · one unit, building or tower on screen: stepping, attack pose, lean, banking,
│                              turret tracking, falling over on death
├─ UnitAnimator.cs       now · ModelRig: swaps a model between its exported poses, aims turret parts, spins rotors
│                              (which parts move comes from the model pack's index, so there is no rig file)
├─ BaseApron.cs          now · the connected base platform
├─ RtsCamera.cs          now · also camera shake (Shake / ShakeAt) since M1.5
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
│  ├─ MapProps.cs        now · base site pads, base aprons, relay towers (by owner), supply crates
│  ├─ Creep.cs           M4 · creep spreading around alien bases
│  ├─ Decals.cs          M4 · craters, scorch marks, tracks
│  └─ Weather.cs         M4
└─ Audio/
   ├─ BattleAudio.cs     M4 · plays sounds for IBattleFx events
   └─ Music.cs           M4

Input/
├─ CommandInput.cs       now in View/ · selection, orders, aim modes · moves here at M2 with Hotkeys
├─ Hotkeys.cs            M2 · every key in one table, rebinding
└─ SecondPlayer.cs       M4 · the co-op Commander (keys or controller)
```

### UI (layer 4)

Built with Unity's UI Toolkit. Each panel is laid out in C#, one small class per panel, and every color and size lives in one style file, `Assets/Outpost/Resources/UI/Outpost.uss`. The placeholder `View/Hud.cs` was removed when `Hud/` replaced it on Nov 12.

```
UI/
├─ Hud/                       now
│  ├─ HudRoot.cs              now · builds the HUD, tells each panel which battle to show, help, pause and end screens
│  ├─ ResourceBar.cs          now · supplies, income, pop, tech, clock, next attack, Commander respawn
│  ├─ SelectionPanel.cs       now · portrait, health, shield, stats, buffs, the build queue; a tile per type for groups
│  ├─ CommandCard.cs          now · the 5 x 3 button grid: orders, abilities, build, turrets, train, research, upgrades, Scan
│  ├─ MinimapPanel.cs         now · with fog and alert pings
│  ├─ AlertsPanel.cs          now · messages, aim-mode banner, toast · M2 adds the "last alert" jump
│  ├─ PowerBar.cs             now · Z X C V leader powers (top centre; aiming goes through CommandInput.powerAim)
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
└─ Shared/
   └─ Ui.cs                   now · element helpers (boxes, text, buttons, bars, portraits) and how
                                    numbers and times are written
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
├─ OutpostMenu.cs       now · the Outpost menu; Effects Test (edit-mode stills of every effect, pool stress test)
├─ HeadlessMatch.cs     now · full matches without graphics, for checking rules changes; scripted players
│                               efficient / steady / casual / idle and BalanceTable() for the difficulty check (M1.9)
├─ ParityCheck.cs       M5 · runs the same scripted fights as the Python game and compares
├─ ContentImport.cs     M2 · one click to re-run the Python export for data, maps or models
└─ BuildTools.cs        M5 · makes the Windows build
```

## Non-code assets

```
Assets/Outpost/
├─ Art/
│  ├─ Materials/     M1.2 · shared materials for the models (today they are made in code)
│  ├─ Shaders/       now · FxAdditive / FxAlpha (effects) · team color and glow (optional, not started; was M1.8) · M4 creep and decals
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

Models, maps and data are produced from the Python project and written into `StreamingAssets/Outpost/`. That stays true until M4, when the map generator is rebuilt in Unity; the sculpting tools for models stay in Python for good.

**The export script.** `tools/export_unity.py` in the Python project was rewritten on Nov 12 (the original was never saved there). `models` turns the sculpts into meshes: every unit in up to three poses (standing, mid-step, attacking), with vehicle turrets, rotors and the heads of base turrets exported as separate parts around their own pivots. `data` copies the data files. `maps` is not rewritten yet; it comes back at M2, when all maps and sizes are exported. Buildings can be re-exported with `models buildings` but still come from the original pack.

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
- **Animation.** Units now animate by swapping exported poses plus movement in code. If that is not enough once you have watched it, `Art/` gains `Models/` and `Animations/` for rigged characters and `UnitAnimator.cs` drives those instead.
