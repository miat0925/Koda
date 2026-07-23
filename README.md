# KODA (MonoGame / C# port)

A port of the browser `koda.html` racer to MonoGame (DesktopGL), same pseudo-3D
road projection, track generation, traffic, checkpoints, timer, and menu, now
running on the GPU via `BasicEffect` + triangle primitives instead of canvas
polygon fills.

## Play in the browser (no install)

The `docs/` folder has the original HTML5/canvas version — just a static
page, no build step. Open `docs/index.html` directly in any browser to play
locally, or enable GitHub Pages for this repo to get a shareable link:

1. On GitHub, go to this repo's **Settings → Pages**.
2. Under "Build and deployment", set **Source** to "Deploy from a branch".
3. Set **Branch** to `main`, folder to `/docs` (that's the only non-root
   option GitHub Pages supports, which is why this folder is named `docs`
   instead of something like `web`), then **Save**.
4. GitHub gives you a URL like `https://miat0925.github.io/Koda/` after a
   minute or two — that's the link to send people.

## MonoGame desktop version

## Important: changes since the first build aren't compiler-verified

The sandbox this is written in has no .NET SDK, no root access to install
one, and no network path to Microsoft's download servers, so nothing here
can be run through `dotnet build` on this end. The project itself builds and
runs successfully (confirmed on the developer's machine); any edits made
after that point are reviewed by hand (brace/paren balance, duplicate method
names, overload-resolution checks) but not compiled here. If a change breaks
the build, paste the error back and it'll get fixed fast.

## Build & run

1. Install the .NET 9 SDK (or newer) if you don't have it:
   https://dotnet.microsoft.com/download/dotnet/9.0

2. From this folder:

   ```
   dotnet tool restore
   dotnet build
   dotnet run
   ```

   `dotnet tool restore` pulls in the MonoGame Content Builder (MGCB), which
   compiles everything under `Content/` (font, audio, logo) into `.xnb`
   files the first time you build.

3. Controls: arrows/WASD to drive, M to mute, Enter to advance
   (menu → car select → race → results → menu).

## Screen flow

```
Ready (title)  --Enter-->  CarSelect  --Enter-->  Playing  --(goal/time up)-->  Results  --Enter-->  Ready
```

- **Ready**: title screen, `koda_menu_theme.ogg`.
- **CarSelect**: pick a body color with Left/Right (A/D), Enter to confirm and
  start the race. Plays `koda_car_select_theme.ogg`. The chosen color is used
  for the player car sprite in `DrawPlayerCar`.
- **Playing**: the race itself, `koda_race_theme.ogg` (unchanged from before).
- **Results**: shown on GOAL! or TIME UP, with final score, checkpoints
  passed, and time left. Plays `koda_results_theme.ogg`. Enter returns to the
  title screen.

## Publishing self-contained builds (to share with other people)

`dotnet run`/`dotnet build` produce a "framework-dependent" build — anyone
running it needs the .NET runtime installed already. A **self-contained**
publish bundles the .NET runtime and all native MonoGame libraries into one
folder, so the other person just needs the folder, nothing else installed.

From this folder, run (each one takes a minute or two, downloads runtime
packs the first time):

```
dotnet publish -c Release -r win-x64   --self-contained true -o publish/windows
dotnet publish -c Release -r osx-x64   --self-contained true -o publish/mac
```

- `publish/windows/` — zip this whole folder. The other person unzips it and
  double-clicks `KodaRacer.exe`. Windows SmartScreen will likely warn
  "Windows protected your PC" since the exe isn't code-signed — they click
  "More info" → "Run anyway".
- `publish/mac/` — zip this whole folder. The other person unzips it and
  double-clicks `KodaRacer` (or runs `./KodaRacer` in Terminal). macOS
  Gatekeeper will block it the first time since it's unsigned — they
  right-click the app → "Open" → "Open" in the dialog (only needed once).
  `osx-x64` runs on both Intel and Apple Silicon Macs via Rosetta 2, which
  is already on virtually every modern Mac.
- If you want a native (non-Rosetta) build for Apple Silicon, add
  `-r osx-arm64` instead of `osx-x64` — the project file already lists all
  three RIDs (`win-x64`, `osx-x64`, `osx-arm64`) so `dotnet restore` fetches
  what it needs for any of them.

There's no equivalent of "send a link" for these — they're downloadable
files, not a webpage. For that, the browser version (`koda.html`) is the
better fit; see the note about hosting it (e.g. itch.io) elsewhere in this
conversation.

## Project layout

```
KodaRacer.csproj      the project file (MonoGame.Framework.DesktopGL)
Program.cs             entry point
Game1.cs                all game logic + rendering
app.manifest            DPI-awareness manifest (Windows)
.config/dotnet-tools.json   pins the MGCB content tool version
Content/
  Content.mgcb          content pipeline manifest
  Fonts/Hud.spritefont   HUD/menu font description
  Audio/koda_menu_theme.ogg
  Audio/koda_race_theme.ogg
  Audio/koda_car_select_theme.ogg
  Audio/koda_results_theme.ogg
  Sprites/koda_logo.png
```

## Things worth knowing about the port

- **Font**: `Fonts/Hud.spritefont` currently points at the system font
  "Arial" (Bold) since no pixel-font `.ttf` was available to bundle. For the
  authentic look, download "Press Start 2P" (Google Fonts, free), drop the
  `.ttf` next to `Hud.spritefont`, and change `<FontName>Arial</FontName>`
  to the file name.
- **Rendering approach**: the browser version fills canvas polygons directly;
  this port batches every quad/triangle for a frame into one
  `VertexPositionColor` list and draws it in a single
  `GraphicsDevice.DrawUserPrimitives` call via `BasicEffect` with
  `VertexColorEnabled = true`. Order matters for the painter's-algorithm look
  (far-to-near), so the draw order in `Game1.Draw` mirrors the JS version
  exactly — don't reorder those calls without checking depth looks right.
- **Sun rendering** is simplified relative to the JS version (two flat
  concentric circles instead of a true radial gradient, and the dark
  "venetian blind" bands are approximated via circle-chord math rather than
  a canvas clip path). Visually close, not pixel-identical.
- **Fullscreen/resize**: the window is resizable and the skyline/star field
  regenerate on resize, same idea as the browser version's `resize()`.
- **Physics/collision/checkpoint/timer logic** is translated line-for-line
  from the already-tested JS (including the windowed collision-span fix for
  high-speed tunneling, and the steering-authority-at-low-speed fix that
  prevented the softlock-against-scenery bug) — this is the part most likely
  to already be correct, since it isn't new logic, just a new host language.

## If something looks wrong at runtime

Since none of this was visually verified (no display in the sandbox either),
after your first successful build it's worth a sanity pass: does the road
curve/hill the way it should, do traffic cars and palm trees/pylons line up
with the road edge, does the menu show over an opaque background, does music
switch between menu/car-select/race/results themes, does Left/Right actually
cycle the car color swatch on the CarSelect screen. Flag anything that looks
off and it can be adjusted quickly now that there's a concrete build to
iterate against.
