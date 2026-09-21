# Translating INI Master

INI Master looks each piece of its text up by the English wording, so a translation is a plain text file with the English on the left and your language on the right. It is the same format Master Looter uses.

## Making a translation

1. In INI Master, choose More, Translate INI Master. It opens `%LOCALAPPDATA%\INIMaster\lang` and puts a fresh `INIMaster.template.txt` there. The same file is `lang/INIMaster.template.txt` in the source.
2. Copy it to `INIMaster.<tag>.txt` in the same folder, where the tag is the language as Windows names it: `de`, `fr`, `ja`, `pt-br`, `zh-tw`. A file for `pt` serves every Portuguese reader who has no `pt-br` or `pt-pt` file.
3. Each line holds the English, a tab, and nothing. Type the translation after the tab. Save the file as UTF-8.
4. Choose More, Language and pick yours. The window switches at once, so you can check each screen without restarting.

A line left empty after the tab keeps the English, so you can translate part of the file and fill in the rest later. Lines starting with `#` are comments. Write a line break as `\n` and a tab as `\t`.

## Placeholders

`{0}`, `{1}` and so on stand for a file name, a number or a message filled in when the text is shown. Keep every one. You can move them, and `Saved {0} to {1} at {2}.` may become `{1}: {0} um {2} gespeichert.` A line that drops or adds one is skipped when the file loads, since it would show the wrong value there. When you pick the language in the menu, the status bar says how many lines were skipped.

Counts come in two forms, one line for exactly one (`1 unsaved change`) and one for any other number (`{0} unsaved changes`). Languages with more plural forms have to pick the wording that reads best for both.

## Where files are read from

INI Master checks `%LOCALAPPDATA%\INIMaster\lang` first, then a `lang` folder next to `INIMaster.exe`, then the exe's own folder. The first match for the language wins. A finished translation can also ship inside the exe: put it in the source's `lang` folder and rebuild.

Right-to-left languages turn the layout around once a translation for them is loaded.

## For developers

Write new text as `Loc.T("English")` in C#, `Loc.Plural(n, "1 thing", "{0} things")` for counts, and `{l:T 'English'}` in XAML (`\'` for an apostrophe). Pass literals only. The test `LocalizationTests.TemplateListsEveryString` fails on anything else, and also when the template is out of date. `scripts/update-lang-template.ps1` rewrites the template from the source.

Text a translator changes never reaches an ini file. Values written to the ini, key names and the `Ctrl`, `Shift` and `Alt` in hotkeys stay in English, because the plugins read them.
