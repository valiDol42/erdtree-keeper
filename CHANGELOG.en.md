# Changelog

[Русский](CHANGELOG.md) · **English**

Versions follow [semver](https://semver.org/):

- **MAJOR** - the familiar way of working, or the settings format, is broken;
- **MINOR** - a capability was added;
- **PATCH** - something was fixed, nothing new.

The version is set in one place, `Directory.Build.props`. The short commit hash is appended
to it, so two builds of the same version are always distinguishable: the hash is visible at
the bottom of the window and in the "About" dialog.

---

## 1.5.1

### Fixed

- **Dark Souls Remastered found no saves although the game was installed.** The program
  looked for them in `%APPDATA%\NBGI\DARK SOULS REMASTERED`, by analogy with the other
  FromSoftware games, while the game puts its save into Documents. Every game now has a list
  of known locations rather than one: the program checks them all and takes the one that
  exists. A Documents folder moved to another drive is handled too - the path comes from the
  system rather than being assembled from the user name.

  Fallback paths were added for the other games as well: Dark Souls II, III, Sekiro and
  Armored Core VI are looked for both in `%APPDATA%` and in Documents, and Prepare to Die
  Edition in three places, allowing for the different spellings of the folder name.

- The "no saves found" message now lists every location that was checked instead of one. A
  single path did not say where to look.

### Verified

- On a machine with the games installed: Dark Souls Remastered was found in Documents
  (`DRAKS0005.sl2`, 4 MB), Dark Souls II as `DS2SOFS0000.sl2`, Dark Souls III as
  `DS30000.sl2`. All three are recognised as a BND4 container and pass the check.
- 158 automated checks, the new ones covering the lookup order for Dark Souls Remastered,
  picking the existing path out of several, and the fallback game folders also being
  rejected as a snapshot folder.

---

## 1.5.0

The program stopped being a save keeper for one game and learned to update itself.

### Added

- **The Dark Souls series and the rest of FromSoftware.** The list at the top of the window
  now holds Elden Ring, Nightreign, Dark Souls: Prepare to Die Edition, Dark Souls
  Remastered, Dark Souls II, Dark Souls III, Sekiro and Armored Core VI. The program knows
  where each of them keeps its saves and tells their files from everything else.

  Each game keeps its own snapshot folder and its own choice of account and file: a shared
  list would mean Dark Souls snapshots mixed in with Elden Ring copies, and a bulk delete or
  an autosave rotation reaching into another game's files.

- **Any Steam game, through the "+ game" button.** The window lists installed games that
  have a cloud save folder, read from Steam's own files on disk (`libraryfolders.vdf` and
  `appmanifest_*.acf`). When the saves live elsewhere the folder can be picked by hand,
  together with the file extensions and, optionally, the game's process name, which lets the
  program warn you that the game is running.

  Everything after that is the same as with Elden Ring: a copy verified by SHA-256, a
  refusal if the game was still writing during the copy, and a mandatory backup before a
  snapshot goes back into the game.

- **Updates from a button.** "Updates" in the title bar asks GitHub about a new version,
  shows the release notes, downloads the archive, verifies it against the published checksum
  and replaces the program files. Settings and snapshots are left alone: only what was in
  the archive is replaced. Checking on start can be turned on, a version can be skipped, and
  going online can be forbidden entirely.

  The installation is carried out by the downloaded program itself, started from a temporary
  folder with a special switch: it waits for the previous copy to close, moves the files and
  starts the updated one. No .bat files: the copying is done by the very file whose checksum
  was just verified against the published one.

  There is one request address and it is written into the code, links inside the answer are
  checked against a list of GitHub hosts, every request goes into the activity log under the
  `NET` tag, and an archive whose checksum does not match is deleted.

- **A "+ time" button for snapshot names.** For games whose saves the program does not
  parse, a location cannot be inserted, and the date and time are the only thing that
  meaningfully tells one snapshot from another.

### Changed

- **The depth of the integrity check is now stated honestly.** For Elden Ring all 11
  checksums are still recomputed. For the other FromSoftware games the BND4 container
  structure is checked: a truncated or foreign file shows up, damaged data inside does not,
  because it is encrypted and only the game holds the key. For a game added by hand, all
  that is known is that the copy is exact. The program says so in the window and in the
  report instead of answering "the file is intact" everywhere.

- **Settings moved.** The snapshot folder and the last account and file are now stored per
  game. Previous values are carried over into the Elden Ring entry on first launch, so the
  list of snapshots stays the same after the update.

- The build check that watched for the absence of networking libraries was replaced by an
  address check: the built exe must carry exactly the GitHub address stated in README.

### Verified

- 155 automated checks, the new ones covering game profiles, parsing Steam's files, the BND4
  container structure, version comparison and parsing GitHub's answer, tampered and broken
  answers included.
- The screenshot harness gained three behaviour checks: switching games really does change
  the folder and the list; without permission, starting the program produces no network
  request at all; installing an update replaces the program files and leaves settings and
  snapshots untouched.
- The whole update path was walked against the live GitHub: the request, the checksums, the
  archive download, the checksum comparison against the published one, the unpacking.

---

## 1.4.1

A pass over speed and reliability. Speed raised no questions: every operation fits into tens
of milliseconds and nothing leaks. The fixes are about what happens when something goes
wrong.

### Fixed

- **An unexpected error closed the program silently.** There was no global exception
  handling at all, and synchronous commands and click handlers caught nothing: a failure on
  the UI thread closed the window without a word. For a program already suspected of being
  malware, "I clicked and everything vanished" is the worst possible outcome. A failure is
  now written to `erdtree-keeper-crash.log` next to the program (attach it to a bug report),
  a system message box explains what happened and where the log is, and after an error on
  the UI thread the program stays open.
- **Background tasks lost their errors.** Re-reading the save after the game writes it and
  taking an autosnapshot both run without being awaited, and any exception other than a file
  one disappeared without a trace. Everything now reaches the activity log, exception type
  included.
- **A save could be read twice at once.** The timer tick and the "+ location" button could
  land in the same second; the second read added nothing but another 29 MB off the disk. A
  latch now prevents it.

### Verified

- Measurements on a real save (27 MB): reading 8-23 ms, parsing 0-13 ms, integrity check
  33 ms, taking a snapshot about 90 ms, restoring with a backup 117 ms, listing 500
  snapshots 15 ms, rotating 500 files down to 10 41 ms. After ten reads in a row memory
  returns to 28 MB, so nothing leaks.
- The built exe: the window appears in 0.8 s, working set about 110 MB.
- The harness deliberately crashes the UI thread and checks that the process survives and
  the crash log is written.

---

## 1.4.0

The location in a snapshot name stopped lagging behind the game.

### Fixed

- **"+ location" and "+ boss" inserted the place the character stood when the program
  started.** The save was read once, and getting the current place meant pressing "Read
  save" by hand every time - with nothing to hint at it: the button worked, it just inserted
  a stale name. The program now watches the file and re-reads it once the game has finished
  writing; the button additionally checks freshness at the moment it is pressed, in case
  less time passed between the write and the click than is needed to call the write
  finished.
- **Watching for writes only worked with autosave enabled.** The same mechanism now runs
  always: the location in a name matters to people taking snapshots by hand too.

### Changed

- The character sheet and the "where now" line update on their own as well - it is the same
  read.
- A new status appeared: "Save re-read - the location is current". A background read does
  not take over the busy indicator and does not get in the way.

### Verified

- The screenshot harness walks both paths on real saves with different locations: swap the
  file and press the button, swap the file and wait without pressing anything. The second
  path completes in 6.5 seconds.
- The first version of that check caught a bug in the logic itself: freshness compared as
  "the file is newer than what we read", while restoring a snapshot puts a file with an
  OLDER write time into the game - and the swap went unnoticed. The comparison now uses a
  fingerprint (time plus length) and tests for inequality.

---

## 1.3.1

Switching the language in an open window only worked halfway.

### Fixed

- **The window was translated in part.** Strings assembled in the model - the character
  sheet, the age of the save, the column headers, the buttons under the list - changed,
  while everything taken from the markup by key stayed in the previous language. The cause:
  a `{Binding L[key]}` binding does not react to the `"Item[]"` notification the string
  table was raising on a language change. The markup now binds to a snapshot of the table,
  so a switch replaces the whole dictionary - an ordinary property change, which bindings
  never miss. The redraw is also announced as a whole instead of by a hand-written list of
  properties; that list is exactly what failed.
- **Reading the settings switched the language for the whole application.** A side effect
  introduced in 1.3.0: `PortableSettings.Load` applied the language, because the default
  folder name is derived from it. Any later call re-languaged an already open window. The
  folder name is now looked up in one language on the spot, and the language is applied
  once - at startup.
- **The source list went blank after a language change.** The "Snapshots" and "Autosaves"
  items were rebuilt from scratch, the field cleared its selection and managed to write
  "nothing selected" back into the model. The items are now renamed in place, and the
  selection is restored after the redraw.
- **The status line froze in the previous language.** It now keeps the key rather than the
  finished text, and is recomputed together with the window. Messages coming from the
  copying service still stay as they were: there is nothing to recompute them from.

### Changed

- **The language switch is a `RU | EN` segment** instead of a single `EN` button. Both
  languages are visible and the current one is marked; clicking the active half does not
  turn anything off.

### Verified

- 90 tests, up from 83. The new ones cover the settings side effect, the language surviving
  a restart, and the snapshot of the string table.
- A screenshot of the window after switching the language in a running window was added to
  the preview set - in both directions. The earlier screenshots were taken on a model
  created in the target language from the start, and so never checked the thing that
  mattered.

---

## 1.3.0

English, in the program and in the repository.

### Added

- **A two-language interface.** The `EN` switch in the title bar changes the language on the
  fly, with no restart; the choice is remembered in the settings. On a first launch the
  language comes from the system: Russian for Russian, Ukrainian, Belarusian and Kazakh
  locales, English for the rest. Everything a player sees is translated: windows, hints,
  operation messages, the activity log, the character sheet, the integrity report and the
  startup message about missing libraries.
- **English README and SECURITY** - [README.en.md](README.en.md) and
  [SECURITY.en.md](SECURITY.en.md), cross-linked with the Russian versions both ways.
- **Translation tests** (36 new, 81 in total): every key has both strings, the English side
  carries no Cyrillic, `{0}` placeholders match between languages, plural forms are picked
  correctly on both sides (in Russian, with the 11-14 exception), and a language change
  raises the notification that redraws the markup.

### Changed

- **Default folder names follow the language:** `Снимки` and `Перед восстановлением` in the
  Russian interface, `Snapshots` and `Before restore` in the English one. Folders that
  already exist are not renamed - the snapshot path lives in the settings, and existing
  copies stay where they are.
- **The digit group separator** is a space in the Russian interface and a comma in the
  English one (`1 084 860` and `1,084,860`).

### Fixed

- **The system language was detected incorrectly.** The program is built without ICU
  (`InvariantGlobalization`), which makes `CultureInfo.CurrentUICulture` always invariant, so
  it never reveals the system language. On Windows it now comes from the system itself
  (`GetUserDefaultUILanguage`), with `CultureInfo` left as the fallback path.

---

## 1.2.1

A review before opening the source. Some of the findings were confirmed by running the code
rather than by reading it.

### Fixed

- **An unusable snapshot could overwrite the game save.** The integrity check treated a file
  as intact when its blocks simply were not found: for a truncated or empty file the list of
  checked blocks stayed empty, and "no damaged blocks" meant "there was nothing to check". A
  zero-length snapshot passed the restore and reported success. A file now counts as intact
  only when all 11 blocks are present, and a restore refuses on any fault - and says which
  one.
- **Autosave rotation deleted other files.** Anything that looked like a save was eligible
  for deletion. Point the autosave folder at the snapshot folder and manual copies
  disappeared; point it at the game folder and `ER0000.sl2.bak` and the save itself did.
  Only files with a timestamp in the name - the ones autosave made - are deleted now.
- **Writing the game save was not atomic.** Writing 29 MB directly truncates the file first,
  and an interruption left a stump behind. The file is now prepared alongside, verified by
  SHA-256 and swapped in with a single move.
- **The backup taken before a restore was not verified.** Its unusability would have come to
  light exactly when the copy was needed. It is now verified, and on a mismatch the restore
  is cancelled before anything is written.
- **The snapshot folder could be pointed at the game folder.** A snapshot named `ER0000`
  would then overwrite the live save, bypassing the mandatory backup. This is now rejected.
- **A corrupt save closed the program without a word.** The parser did not catch every
  exception, and a failure inside an asynchronous command was not caught at all. The error
  is now shown in a window.
- **Starting without the libraries was silent.** Extract only the exe from the archive and
  the process died before any window appeared. A system message now lists the missing files.
- The window shrinks to fit the screen: 1180x900 logical units did not fit on 1366x768, nor
  at 150 percent scaling.

### Security

- Real SteamID64 values were removed from the tests - they resolve to a public profile. They
  were replaced with numbers below the range of existing accounts (real ones start at
  76561197960265729). The repository history was rewritten before the source was opened, and
  the previous values did not survive.

### Tests

- 45 instead of 30. `SnapshotService` gained coverage: a backup before overwriting, refusal
  of empty, truncated, damaged and foreign files, and rotation touching only its own files.
  These checks need no real saves and therefore run in CI.

## 1.2.0

### Added

- The player card: the character block on the main screen is clickable and opens everything
  that could be read from the save - eight stats, HP, FP and stamina, runes held and earned
  in total, time played, Shadow of the Erdtree blessings, and the nearest grace and boss.
  The data comes from the file alone, with no requests going anywhere.
- The level in the card is cross-checked against the sum of invested points. If they
  disagree, the window says so: a mismatch would mean the structure parsing has drifted, and
  the numbers next to it cannot be trusted.

### Removed

- The hint under the freshness indicator ("the save is old", "rest at a site of grace"). The
  colour and the write time say the same thing, and the line took up space.

## 1.1.0

### Added

- Bulk operations: several snapshots can be selected in the list and deleted at once.
  "Select all" and "Clear selection" buttons, a counter of what is selected next to them,
  and the number of files on the delete button. The confirmation lists the names rather than
  just the count: deletion is irreversible, and "delete 7 files" gives no chance to notice
  something extra.

### Fixed

- The selected file in the list was barely visible: the highlight differed from an ordinary
  row by a couple of shades. Three signs now show the selection at once - a checkmark, a
  golden bar on the left, and a filled row.
- The selection vanished from the screen after every list refresh. The selection flag moved
  into the row itself: SelectedItems and Selection must not be mixed in Avalonia, touching
  one puts the list into that mode, and the other stops affecting what is shown.

## 1.0.0

The first version. It replaces the earlier PowerShell script, which was launched through a
`.bat` file and left a black console window behind.

### Capabilities

- Copying saves with SHA-256 verification and a re-read of the source: if the game was
  writing the file during the copy, the snapshot is cancelled.
- A save integrity check - all 11 MD5 checksums recomputed, with a report per block. Damage
  is visible before a restore, not after.
- A snapshot name taken from the save itself: the nearest site of grace or boss arena, with
  the DLC tag added automatically.
- A save freshness indicator: it shows whether the game has written the save to disk.
- Autosave on the fact of a write by the game, not on a timer. The frequency (by default no
  more than once every 5 minutes), the number of copies to keep (10) and the folder are
  configurable.
- Restoring into the game with a mandatory backup of the current save.
- List sorting by name and by date, with numbers compared as numbers.
- An activity log of file operations, exportable to text.
- Support for several Steam accounts and for the `.co2` files of the Seamless Co-op mod.

### On trust

- The built file imports no networking library; CI fails the build if one appears.
- Save contents are not modified - the file is copied whole.
- No administrator rights are requested.
- Settings sit next to the program, in a single file.
- The "What this program does" window lists everything the program touches on disk.
