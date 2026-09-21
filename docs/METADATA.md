# Adding INI Master help to a mod

INI Master finds every ini in `bin64` (and in `bin64\scripts` and `bin64\plugins`, where Ultimate ASI Loader also looks) and turns it into a settings page. It pairs `MyMod.asi` with `MyMod.ini`. Without any work from you it already reads your comments. The three ways below give it more, and a later one overrides an earlier one field by field:

1. comments and `;@` directives in the shipped ini
2. a sidecar file, `MyMod.inimeta`, next to the ini
3. metadata embedded in `MyMod.asi`

Embedding is the one to use if you can. The help then ships inside the plugin, it can't drift out of step with the code, and players who replace their ini with an old copy still get current help.

## 1. What the tool reads from plain comments

The comment block sitting directly above a key, with no blank line between them, is that key's help. The first sentence shows under the setting's name, and the whole block appears in the tooltip and the details pane.

```ini
; Drains that run for as long as you hold them: sprinting, climbing,
; swimming, the glider.
ContinuousPercent=0
```

A key with no comment of its own, directly under one that has one, shares it (a key and its controller combo, say).

Choices are read from the layouts people already write:

```ini
; The flying ceiling.
;   0        leave the game's 1350.0
;   -1       no ceiling
;   <number> that height, e.g. 5000
Ceiling=-1
```

The indented `value  label` rows become a dropdown. A `<placeholder>` row means other values are allowed too, so a text box sits beside the dropdown. Two rows whose values are `0` and `1` with short labels (`off`, `on`) become a checkbox.

`0 = ULTRA, 1 = HIGH, 2 = MEDIUM` and `0: Input | 1: Present` on one line also become a dropdown, and so does OptiScaler's `a, b, c - Default (auto) is b`.

Other things it picks up:

- `; ------------ marking` starts a group heading named "marking" within the section.
- A comment block followed by a blank line is shown as a note in place.
- The comments at the top of the file are the mod's description.
- A single range such as `1-100` or `100 to 10000` in the help becomes a slider. The tool only warns when a value falls outside a range it read from prose. It never refuses one.
- "Takes effect next start" marks a key as needing a restart. "Picked up while the game runs" marks the whole mod as reloading its ini.
- `true`/`false`, `on`/`off` and `yes`/`no` values are checkboxes. A `0`/`1` value becomes a checkbox when its help starts with "1 turns...", "1 makes...", or when the key's name looks like a switch (`Enabled`, `ShowHud`, `HookXInput`).
- A key named like a hotkey (`MenuKey`, `KeyToggle`, `ToggleKey`) with a virtual-key code (`45`) or a key name (`F11`, `Ctrl+F1`) gets a box that records the next key you press, and writes it back in the same form.

Players can switch to plain text values or the File text tab whenever a guess is wrong.

## 2. `;@` directives in the ini

A comment line that starts with `;@` (or `#@`) gives exact instructions. Game plugins skip it like any other comment. Put it in the block above the key it describes:

```ini
; Costs charged once per use: a roll, a jump, each swing of a weapon.
;@ type=int min=0 max=100 step=5 unit=% label="Use cost"
UsePercent=0

;@ type=enum options=0:Off|1:Nearby only|2:Everywhere
Mode=1

;@ advanced restart
HookDX12=1
```

A value without quotes runs until the next ` name=`, so `label=Use cost unit=%` works. Bare words set flags: `advanced`, `hidden`, `readonly`, `live`, `restart`, `custom`, or a type name.

Above a section header, `;@` lines describe the section: `label`, `description`, `hidden`, `advanced`, `order`. Anywhere, `;@mod` describes the mod:

```ini
;@mod name="Stamina Master" version=1.0.1 author=Seth live=1 url=https://www.nexusmods.com/crimsondesert/mods/3549
```

## 3. The `.inimeta` file

Name it after the ini (`MyMod.inimeta`) and put it next to it. It can hold either of two things.

**An annotated default ini.** Your documented default ini, with `;@` lines where you want them. Every value in it is read as that key's default, which gives players a reset button. It also lets INI Master write the ini from scratch if a player deleted it. If your plugin already embeds its default ini to write on first run, this is the same file.

**JSON.** Comments and trailing commas are allowed. Every field is optional:

```jsonc
{
  "inimeta": 1,
  "name": "Example Mod",
  "version": "1.0.0",
  "author": "You",
  "url": "https://www.nexusmods.com/crimsondesert/mods/0000",
  "ini": "ExampleMod.ini",         // which ini this describes; default: the plugin's name
  "live": true,                    // the plugin rereads its ini while the game runs
  "description": "Shown under About this mod.",
  "sections": {
    "settings": {
      "label": "General",
      "description": "Shown under the section title.",
      "order": 1,
      "keys": {
        "Speed": {
          "label": "Travel speed",
          "type": "float",           // bool, int, float, enum, key, string
          "min": 0.5, "max": 4, "step": 0.25, "unit": "x",
          "default": "1.0",
          "group": "Movement",
          "help": ["How fast you move.", "", "Above 2 the camera struggles."],
          "tooltip": "Optional short text for the hover tooltip instead of help.",
          "live": true,              // per key; false shows a "Next launch" badge while the game runs
          "advanced": false,         // shown only with Show advanced
          "hidden": false,
          "readonly": false,
          "order": 3
        },
        "Quality": {
          "type": "enum",
          "options": [ { "value": "0", "label": "Low" }, { "value": "1", "label": "High" } ],
          "custom": false            // true allows values outside the list
        },
        "Enabled": { "type": "bool", "true": "1", "false": "0" },
        "MenuKey": { "type": "key", "format": "name" },   // vk (45), hex (0x2D) or name (Ctrl+F1)
        "Notes": "A bare string is the help text."
      }
    }
  }
}
```

`options` can also be an object (`{ "0": "Low", "1": "High" }`) or a string (`"0:Low|1:High"`). Keys the ini doesn't have yet still show, with their default, and a change adds them to the right section.

**More, Export metadata template...** in INI Master writes this JSON for the selected ini, filled with everything the tool worked out from your comments. Start from that.

## 4. Embedding it in the plugin

INI Master reads the plugin's bytes from disk. It never loads the DLL, so no plugin code runs and the game can have it loaded at the same time.

### With a resource script (recommended)

Add one line to your `.rc` file and ship the `.inimeta` inside the plugin:

```rc
INIMETA INIMETA "MyMod.inimeta"
```

The resource type must be `INIMETA`; the name can be anything. With CMake and MSVC, add the `.rc` file to the target's sources. There is no size limit. A plugin that describes more than one ini carries one resource per ini, each with its own `"ini"` field.

### Without one

Include `sdk/inimaster.h` and put this in exactly one `.cpp` file:

```cpp
#include "inimaster.h"

INIMASTER_EMBED_META(R"json({
  "name": "My Mod",
  "live": true,
  "sections": { "settings": { "keys": { "Speed": { "min": 0.5, "max": 4 } } } }
})json")
```

The macro wraps the text in `@@INIMETA@@ ... @@/INIMETA@@` in a section of its own and tells the linker to keep it. Any toolchain can do the same by hand: put those two markers around UTF-8 text somewhere in the binary, with no NUL byte between them. MSVC limits a single string literal to about 16 KB, so split longer text into adjacent literals, or use the resource route.

`sdk/example` is a plugin that does both, with `build.bat`.

## 5. Reloading while the game runs

INI Master saves each change a moment after it's made (players can turn that off). It writes the new file beside the old one and renames it into place, so a plugin reading at that instant gets one whole file or the other. Each save starts from the file on disk and changes only the keys the player edited. A plugin that rewrites its own ini, as Master Looter's in-game menu does, keeps its changes, and INI Master reloads the page when it sees the file change.

To pick edits up without a restart, check the ini's last-write time from a worker thread every quarter second or so and reread it when the time moves. `inimaster::IniWatcher` in `sdk/inimaster.h` does exactly that:

```cpp
static inimaster::IniWatcher watch(inimaster::BesideThisModule(L"MyMod.ini"));
if (watch.Changed()) ReadSettings();
```

Then set `"live": true`, and players see that their changes apply at once. Mark anything read only at startup, such as hooks installed once, with `"live": false`.
