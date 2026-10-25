---
name: add-string
description: Add, rename or remove a user-visible text in GBFileCopier - interface strings (Strings.resx) or engine messages (Messages.resx) - in English and Italian, with the accessor property and its use from XAML or C#. Use whenever a change shows new text to the user in the app or in an engine error message.
argument-hint: <what the text is for, e.g. "Clear list" button in the main window>
---

# Add a localized text: $ARGUMENTS

Every text the user sees comes from a `.resx` resource, in English and Italian. A text is added in
**three files**, always together; the tests fail if one is missing.

## 1. Choose where it goes

| Text | Neutral (English) | Italian | Accessor |
|---|---|---|---|
| App interface (windows, menus, buttons, tooltips, status lines) | `src/GBFileCopier.App/Resources/Strings.resx` | `Strings.it.resx` | `Strings.cs` |
| Engine messages (copy errors, warnings from `GBFileCopier.Core`) | `src/GBFileCopier.Core/Resources/Messages.resx` | `Messages.it.resx` | `Messages.cs` |

The command line (`GBFileCopier.Cli`) is **not** localized: its output stays in English, inline.

Before adding, search both `.resx` files for an existing text with the same meaning and reuse it.

## 2. Name

PascalCase, describing the role, not the words: `ClearList`, `ConflictTitle`, `VerifyingFile`.
Related texts share a prefix (`Theme`, `ThemeLight`, `ThemeDark`; `About…`; `Menu…`). The name
must be unique in its file.

## 3. Write the three entries

In both `.resx` files, add the entry in the same position (next to related texts), 2-space indent:

```xml
  <data name="ClearList" xml:space="preserve">
    <value>Clear list</value>
  </data>
```

In the accessor class, at the matching position, a property whose name is the key and whose summary
is the English text verbatim (placeholders included):

```csharp
    /// <summary>Clear list</summary>
    public static string ClearList => Get();
```

`Get()` reads the resource named after the property (`[CallerMemberName]`): no string literal.

Escape `&`, `<`, `>` in the XML as `&amp;`, `&lt;`, `&gt;`.

## 4. Text conventions

- **Placeholders**: `{0}`, `{1}` in the same set in both languages (the tests compare them); use
  them only through `string.Format(CultureInfo.CurrentCulture, Strings.Name, …)`. Never build a
  sentence by concatenating translated fragments: word order changes between languages.
- **Ellipsis**: the single character `…` (not `...`) for commands that open a dialog
  (`Add files…`) and for ongoing states (`Cancelling…`).
- **Italian**: natural Italian, not word for word. Buttons and commands in the imperative
  (`Annulla`, `Aggiungi cartella…`, `Mostra tutti i file`), straight apostrophe `'` as in the
  existing texts (`l'originale`), accents written as accented letters (`già`, `è`, never `e'`).
  Keep the terminology already used: copia/sposta, origine/destinazione, verifica, cartella.
- **Same text in both languages** (e.g. `Checksum`): allowed only if added to the
  `sameInBothLanguages` list of the test (`tests/GBFileCopier.App.Tests/LocalizationTests.cs` for
  `Strings`, `tests/GBFileCopier.Core.Tests/MessagesTests.cs` for `Messages`); otherwise the test
  fails as "not translated".
- Keys shown with keyboard shortcuts must be right on both platforms (⌘ on macOS, Ctrl on Windows):
  pick the symbol in code, or write a text that covers both, as existing texts do.

## 5. Use it

- XAML (the view declares `xmlns:res="using:GBFileCopier.App.Resources"`):
  `Text="{x:Static res:Strings.ClearList}"`.
- C#: `Strings.ClearList`, or `string.Format(CultureInfo.CurrentCulture, Strings.Name, value)`;
  in Core, `Messages.Name`.

## Rename or remove

Rename or delete the key in **both** `.resx` files and the property together, and update every use
(`grep -rn "Strings.OldName\|res:Strings.OldName" src`). Remove unused texts: an orphan resource
with no property fails the tests.

## 6. Check

```bash
dotnet build
dotnet test --project tests/GBFileCopier.App.Tests --filter-class "*.LocalizationTests"   # Strings
dotnet test --project tests/GBFileCopier.Core.Tests --filter-class "*.MessagesTests"      # Messages
```

The tests run on Microsoft.Testing.Platform with xunit v3: VSTest's `--filter` is not accepted, and a
class filter over the whole solution fails with exit code 8 on projects with no matching test.

If the text appears in a window, the App tests' PNG renders
(`tests/GBFileCopier.App.Tests/bin/*/net10.0/screenshots/`) show whether it fits; Italian texts are
usually longer.
