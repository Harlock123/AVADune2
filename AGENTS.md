# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
its world; this file covers how the code is put together and the rules that keep it working.

## What this is

A real-time strategy game in the style of Westwood Studios' 1992 Dune II, set in a world of its own (in the game
it is called The Solarite Wars: Battle for Vharos). It is written in C# on .NET 10 and Avalonia 12, drawn with
Avalonia's `DrawingContext` and pixel-painted bitmaps. One self-contained executable per platform (Windows, macOS
and Linux, x64 and ARM). The graphics are drawn in code, and the sounds, music and advisors' voices are
synthesised by `tools/make_sounds.py` (with `tools/speech.py`).

## Commands

```bash
dotnet build AVADune2.slnx                        # the SDK version is in global.json (10.0.400 or later)
dotnet test AVADune2.slnx                         # every test runs headless: no window opens
dotnet run --project src/AVADune2                 # play it; --no-audio for silence
DUNE_SHOTS=$PWD/Images dotnet test tests/AVADune2.Tests --filter ScreenshotTests   # refresh the README's pictures
DUNE_PROBE=1 dotnet test tests/AVADune2.Tests --filter PilotProbe   # how the computer player gets on in the campaign
python3 tools/make_sounds.py                      # rebuild src/AVADune2/Sounds (standard-library Python only)
```

The probe also reads `DUNE_MISSIONS` (how many, default 4), `DUNE_FROM` (first mission), `DUNE_HOUSE` (0 to 2),
`DUNE_ARMY` and `DUNE_TRACE`; `DUNE_PROBE=lazy` leaves the player's side idle.

`Directory.Build.props` sets `net10.0`, `TreatWarningsAsErrors`, nullable reference types and implicit usings for
every project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVADune2/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, choice of House, briefing, battle, debrief, territory
    map, high scores) and the fixed-step loop.
  - `World.cs` has the battlefield's measures (`Field`), the seeded `Dice` and the `World` itself.
  - `Sim.cs`, `Sim.Units.cs` and `Sim.Combat.cs` are the battlefield rules, one 1/60 s step at a time.
  - `Rules.cs` has the Houses and the numbers for units and buildings. `Campaign.cs` has the missions and the
    territories.
  - `MapGen.cs` makes a battlefield from a mission. `PathFinder.cs` is A* over the tiles.
  - `Brain.cs` is a House's mind, for the computer's Houses. `Pilot.cs` puts one in charge of the player's side,
    for the demonstration and the tests.
  - `InputHandler.cs` (keys, mouse) and `ControllerManager.cs` (an SDL2 gamepad) produce `Controls`.
  - `SaveFile.cs` saves the campaign between missions. `HighScoreTable.cs`, `InitialsEntry.cs` and
    `SoundManager.cs` do what their names say.
- `src/AVADune2/Views/`: `GameCanvas` (split over partial files: map, units, sidebar, screens) draws the fixed
  1024x768 frame, runs the 60 Hz timer and turns mouse clicks and one-shot keys into orders.
  - `MapPainter.cs`, `BuildingArt.cs`, `Portraits.cs` and `Pix.cs` paint the ground, buildings and advisors pixel
    by pixel.
- `src/AVADune2/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVADune2.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `TestWorld.cs` builds small battlefields and
  games for the tests. `PilotProbe.cs` does nothing unless `DUNE_PROBE` is set.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` advances the battle in fixed 1/60 s steps (`Field.Step`, `Sim.Step`),
  independent of the frame rate (the game-speed setting scales the time fed in). Draw random numbers from the
  world's seeded `Dice` (`w.Dice`), never from `Random`, so a seed always plays the same battle.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` reads the world, draws it, and turns clicks into engine calls.
- **Input goes through `InputHandler`.** Map scrolling on keys, D-pad and right stick must produce the same `Dy`
  sign the engine expects (-1 is up the screen). The gamepad's left stick moves its own cursor.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores and the saved campaign (`campaign.sav`) live in the
  app-data folder (`SpecialFolder.ApplicationData/AVADune2`). Tests pass a fresh temporary folder
  (`TestWorld.TempFolder()`) for both and set `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features and controls) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
After a release it asks the portfolio site (Harlock123.github.io) to rebuild, if `SITE_REFRESH_TOKEN` is set.
