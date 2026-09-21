# INI Master

A settings editor for Crimson Desert ASI plugins. It finds the game, lists every plugin in `bin64` next to its ini file, and turns each ini into a page of checkboxes, sliders, dropdowns and hotkey boxes, with the mod maker's own comments as tooltips and help.

It works while the game is running. Changes save to the ini a moment after you make them, and a plugin that rereads its ini picks them up in game. For one that reads its ini only at startup, the page says so and marks those settings "Next launch".

## Using it

Run `INIMaster.exe`. It finds the game through Steam's library list, Epic's manifests or the usual folders. If it misses, press Game folder and pick the Crimson Desert folder or `bin64`.

- Pick a mod on the left. Hover a setting for its full help, or click it to pin the help at the bottom.
- "Save as I edit" writes each change straight away. Turn it off to collect changes and save with Ctrl+S (Ctrl+Shift+S saves every file).
- The dot next to a setting marks an unsaved change. The two buttons on its right undo that change, or put the default back when the mod declares one.
- "Plain text values" swaps every control for the raw text, for when a guess about a setting's type is wrong. The File text tab edits the whole file.
- The first save of each file in a session copies the old file to `%LOCALAPPDATA%\INIMaster\backups`. More, Open backups of this ini goes there.

Saving rewrites only the values you changed. Comments, blank lines, key order, spacing, line endings and encoding stay exactly as they were. If a plugin rewrites its own ini while INI Master is open, the page reloads with the new values and keeps your unsaved edits on top.

## For mod makers

INI Master already reads the comments in your shipped ini. To give it exact types, ranges, labels and defaults, you can add `;@` lines to the ini, ship a `MyMod.inimeta` file, or embed that file in the plugin with one line in your `.rc`:

```rc
INIMETA INIMETA "MyMod.inimeta"
```

[docs/METADATA.md](docs/METADATA.md) has the full format. `sdk/inimaster.h` holds a macro for embedding without a resource script and a small watcher for rereading the ini while the game runs. `sdk/example` is a working plugin that uses both. In the app, More, Export metadata template writes a starting `.inimeta` from what it already reads out of your comments.

Community `.inimeta` files for mods whose authors haven't written one go in the `inimeta` folder next to `INIMaster.exe`.

## Building

Needs the .NET 10 SDK.

```bash
dotnet test tests/IniMaster.Tests
```

```bash
pwsh ./publish.ps1
```

`publish.ps1` writes `dist/INIMaster.exe`, a single self-contained file that needs no .NET install, and `dist/INIMaster-<version>.zip` holding that exe with the docs and the SDK. `sdk/example/build.bat` builds the example plugin with MSVC Build Tools 2022.

## Layout

    src/IniMaster.Core    ini parsing and writing, comment reading, metadata, plugin scanning
    src/IniMaster         the WPF app
    tests                 xUnit tests, including a compiled example plugin
    sdk                   inimaster.h and the example plugin
    docs/METADATA.md      the metadata format
    inimeta               community .inimeta files shipped with the app
