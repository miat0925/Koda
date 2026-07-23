# KODA

A retro cyberpunk pixel-art racing game — pseudo-3D OutRun-style road with
hills, curves, traffic, checkpoints, and a timer.

## Play in the browser

https://miat0925.github.io/Koda/

(or open `docs/index.html` directly in any browser)

## Desktop version (Windows / Mac)

Built with MonoGame (C#).

```
dotnet tool restore
dotnet build
dotnet run
```

### Controls

- Up / W — accelerate
- Down / S — brake
- Left / A, Right / D — steer
- M — mute
- Enter — advance (menu → car select → race → results → menu)

## Project layout

```
KodaRacer.csproj
Program.cs
Game1.cs
Content/          fonts, audio, sprites
docs/             browser version (GitHub Pages)
```
