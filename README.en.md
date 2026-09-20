# Erdtree Keeper

[Русский](README.md) · **English**

A save file keeper for Windows: it copies your saves, shows where your character is
standing, and can put any copy back into the game.

It knows **Elden Ring**, **Nightreign**, the whole **Dark Souls** series, **Sekiro** and
**Armored Core VI**, and any other Steam game can be added by hand - all it needs is the
folder with that game's saves.

Part of [eldenring.krut.top](https://eldenring.krut.top) - an Elden Ring map and progress
tracker.

One portable exe. Settings sit next to it. It goes online only when you tell it to - for
updates, and only if you allowed that.

The interface comes in Russian and English - the `RU | EN` switch in the title bar changes
it without a restart, and the choice is remembered. On first launch the language follows
the system.

---

## Why you can run this

Programs that reach into your save files deserve suspicion: they get downloaded from
forums, built by nobody in particular, and often ask you to turn off your antivirus.
Everything here is arranged so that you do not have to take anyone's word for it.

**The source is open, all of it.** No prebuilt binaries of unknown origin, no obfuscation.
Everything the program does can be read in this repository.

**The file has been scanned by antivirus engines.** The 1.3.1 build was run through
[VirusTotal](https://www.virustotal.com/gui/file/9a532a7d6dbe01776179ce20ea0cd0d929a5952c9bb082a03d9f91c521d9bb4f) - 69 engines at once, and none of them considers the file malicious. The
report is tied to the checksum rather than to my word: compute the SHA-256 of your own copy
and open the report for that sum - the result should be the same. Every new version has its
own checksum and its own report.

VirusTotal shows the name `ErdtreeKeeper.dll` even though the file is an `.exe`. That is the
internal .NET assembly name stored inside the exe; it is the same file, and the matching
checksum confirms it.

**The program goes online only for updates.** There is one address and it is written into
the code - `https://api.github.com/repos/valiDol42/erdtree-keeper/releases/latest`. Links
inside the answer are checked against a list of GitHub hosts, the downloaded archive is
verified against the published checksum, and every request goes into the activity log under
the `NET` tag, with the full address. Nothing about you is sent: no identifiers, nothing
about the machine, nothing about the saves.

You are asked once, the first time the check is used, and the answer is remembered. Without
that permission the program opens no connection at all, which any connection monitor shows -
TCPView or the Windows resource monitor. The imports carry `WS2_32.dll` and `CRYPT32.dll`
for the connection.

**The program does not alter save contents.** It copies the file whole, byte for byte, and
verifies the copy by SHA-256. It only looks inside the save - to show the character name and
to put the location into the file name.

**No administrator rights are requested.** Everything the program touches lives in your own
user folder. It does not modify the registry, the game files or system settings.

**Nothing is left behind in the system.** No installer, no service, no startup entry.
Uninstalling means deleting the folder.

### How to check all that

| What to check | How |
| --- | --- |
| The file was not swapped after the build | In the "About" window press "Compute SHA-256" and compare it with the sum on the release page |
| The build came from this code | Build it yourself and compare the sum. A public repository also carries an attestation: `gh attestation verify ErdtreeKeeper.exe --repo <owner>/erdtree-keeper` (GitHub issues none for user-owned private repositories) |
| The file is not flagged as malicious | [VirusTotal report](https://www.virustotal.com/gui/file/9a532a7d6dbe01776179ce20ea0cd0d929a5952c9bb082a03d9f91c521d9bb4f) for 1.3.1: 0 of 69 engines. Easy to repeat - compute the SHA-256 of your copy and open the report for that sum |
| The program does not go online on its own | Any connection monitor - TCPView, Wireshark, the Windows resource monitor: until you allow update checks and press the button, there are no connections. Every request also shows in the activity log under the `NET` tag |
| What the program does to your disk | The "Activity log" button in the window: every file access is there, and the log exports to a text file |
| The code does what it says | Build it yourself: `dotnet publish` (see below), compare your build with the released one |

A word about antivirus software: for unsigned programs Windows shows the blue SmartScreen
window, "Windows protected your PC". That is not a sign of a virus - it means the file has
no reputation yet. If the window bothers you, build the program from source yourself; the
instructions are below.

---

## What the program does

**Reads**

- The save folder of the selected game and the save files inside it. For Elden Ring that is
  `%APPDATA%\EldenRing` with its `.sl2` and `.co2` files, for Dark Souls III it is
  `%APPDATA%\DarkSoulsIII`, for Dark Souls Remastered it is `Documents\NBGI\DARK SOULS
  REMASTERED`; for a game added by hand, the folder you picked. A game can have several
  known locations and the program checks them all, including the profiles of other Windows
  users on this computer - the game may have been played under another account. A profile it
  cannot read is skipped; no administrator rights are requested.
- Steam's own files on disk (`libraryfolders.vdf`, `appmanifest_*.acf`) - only while the
  "Add a game" window is open, to list the installed games.
- Files are opened read-only, in a sharing mode that does not disturb the game.

**Writes**

- Save copies into the folder you pick (by default `Snapshots` next to the program), and
  autosaves into the `autosave` subfolder inside it - or any other folder you name.
- Settings into a single file, `erdtree-keeper.settings.json`, next to the program.
- Into the game folder exactly once - when you press "Restore to game". Before that the
  current save always goes into the `Before restore` subfolder.

**Does not**

- Does not reach the internet without permission, and with permission only GitHub, for the
  list of releases. It sends nothing about you while doing so.
- Does not alter save contents.
- Does not ask for administrator rights.
- Does not add itself to startup.
- Does not touch game files, the registry or system settings.

---

## Features

**Several games.** The list at the top of the window holds Elden Ring, Nightreign, Dark Souls
(Prepare to Die, Remastered, II, III), Sekiro and Armored Core VI. Each game keeps its own
snapshot folder and its own choice of account and file, so copies of different games never
share a list and a bulk delete cannot reach across.

The "+ game" button adds any other Steam game. The window lists installed games that have a
cloud save folder - read from Steam's own files on disk - or the folder can be picked by
hand when the saves live elsewhere. Everything after that is the same: a copy, a SHA-256
check and a mandatory backup before the file goes back to the game.

**Update check.** The "Updates" button in the title bar asks GitHub about a new version,
shows the release notes, downloads the archive, verifies it against the published checksum
and replaces the program files - settings and snapshots are left alone. No network is used
until you allow it; the details are in "Why you can run this".

**Save freshness indicator.** The game does not write the save to disk immediately, so a
copy taken at the wrong moment misses the last events. The program shows the age of the file
and says outright when it is worth resting at a site of grace so the game saves.

**A name taken from the save itself.** The "+ location" and "+ boss" buttons read the
character position and insert the nearest site of grace or boss arena. The `DLC` tag for the
Realm of Shadow is added on its own. Names stay consistent between files, and `_before` /
`_after` pairs always end up next to each other when sorted.

The place is always current: the program watches the file and re-reads it once the game has
finished writing the save, and the button checks freshness at the moment it is pressed.
There is no need to press "Read save" before every snapshot - that button remains for when
you simply want to look at the character card right now.

**Integrity check.** How deeply a file is checked depends on the game, and the window says
so plainly. For Elden Ring - completely, all 11 checksums. For the other FromSoftware games
the BND4 container structure is checked: a truncated or foreign file shows up, damaged data
inside does not, because it is encrypted and only the game holds the key. For a game added
by hand, all that is known is that the copy is exact by SHA-256, and the program does not
pretend to know more.

For Elden Ring it recomputes all 11 checksums inside a save and reports, block by
block, whether each one matched. Damage becomes visible in advance instead of at the moment
the game says "Save data is corrupt".

**Autosave.** A snapshot is taken not on a timer but on the fact of a write: the program
waits until the game has written the save and the file has stopped changing. The frequency
(by default no more often than once every 5 minutes), how many copies to keep (10 by
default) and the folder - which can live on another drive - are all configurable. Old
autosaves beyond the limit are deleted; manual copies are never touched. The list on the
right switches between manual snapshots and autosaves, and you can restore from either
folder.

Those minutes are a lower bound on frequency, not a schedule. A plain timer would regularly
catch either a file mid-write or a copy of the very same state.

**Safe restore.** Before the game save is overwritten the current file always goes into a
backup - that cannot be turned off. If the game is running, the program warns you: the game
holds the save in memory and will overwrite the file on exit.

**Several accounts.** Folders of different Steam accounts are visible at once, and each one
can be given a human name instead of a long number.

**Seamless Co-op.** `.co2` files are supported alongside the ordinary ones.

**Two languages.** Russian and English, switched by the `RU | EN` control in the title
bar. The choice is saved; on first launch the language follows the system.

---

## Installation

1. Download the archive from the releases page.
2. Extract it into any folder - for example `D:\Programs\ErdtreeKeeper`.
3. Run `ErdtreeKeeper.exe`.

There is no installer and there will not be one: the program is portable. Settings sit next
to it, so the folder can be moved to another drive or a flash drive with all settings
intact.

If you put the program into `Program Files` it will not be able to write next to itself, and
the settings will go to `%APPDATA%\ErdtreeKeeper` - the "About" window says so when that
happens.

Requirements: Windows 10 or newer, 64-bit. Nothing to install alongside - .NET is inside.

### Updating

The easy way is the "Updates" button in the title bar: the program downloads the new
version, verifies it against the published checksum, replaces its own files and opens
again. Settings and snapshots are left alone. Update checks have to be allowed first - you
are asked once, in a window of its own.

By hand it looks like this: close the program and replace `ErdtreeKeeper.exe` and the
`.dll` files with the new ones. Nothing else needs touching.

**Do not delete the whole folder before extracting.** Your settings
(`erdtree-keeper.settings.json`) live next to the program, and by default so does the
`Snapshots` folder with all your copies. Deleting the folder takes both with it.

If you update from source, there is a script that replaces only the program files. It can be
run from any folder, by full path:

```powershell
powershell -ExecutionPolicy Bypass -File "F:\path\to\erdtree-keeper\tools\update-install.ps1" -Target "D:\Programs\ErdtreeKeeper"
```

It refuses to work while the program is running, and touches neither settings nor snapshots.

`-ExecutionPolicy Bypass` is needed because Windows forbids running scripts by default. The
script is short - read it before running; there is nothing in it beyond copying files.

---

## Important: Steam Cloud

Steam synchronises the Elden Ring save folder with the cloud. If you swap the file while the
game or Steam is running, the cloud version can come back and overwrite the restored save.

So the order is:

1. Close the game completely - not minimise, quit.
2. Restore the snapshot.
3. Start the game.

The program warns you when it sees the game running or cloud sync enabled for the selected
account.

---

## About online bans

The program copies your own files and does not change their contents - the same thing you
would do by copying a save in Explorer. Nobody can give guarantees about anti-cheat
behaviour, and neither will we: online is FromSoftware's domain, not this program's.

Common sense: do not restore old saves while playing online, and do not use save editors if
you play with other people.

---

## Building from source

You need the [.NET SDK 10](https://dotnet.microsoft.com/download) and Windows.

```bash
git clone <repository address>
cd erdtree-keeper
dotnet test
dotnet publish src/ErdtreeKeeper/ErdtreeKeeper.csproj -c Release -r win-x64 -o out
```

The `out` folder will hold `ErdtreeKeeper.exe` and three native rendering libraries. That is
exactly what ships in a release.

The build runs in NativeAOT mode: the output is an ordinary native exe with no intermediate
runtime, and it unpacks nothing into temporary folders on start.

---

## How the save file is laid out

These findings were verified against real files rather than taken from descriptions on the
web - see `tests/ErdtreeKeeper.Core.Tests`.

`ER0000.sl2` is a BND4 container of 28,967,888 bytes:

| Offset | Size | What it is |
| --- | --- | --- |
| `0x000000` | `0x300` | BND4 header |
| `0x000300` | 10 × `0x280010` | Character slots: 16 bytes of MD5, then `0x280000` bytes of data |
| `0x19003A0` | `0x60010` | Profile block: 16 bytes of MD5, then `0x60000` bytes of data |

The game checks the MD5 of each block and refuses to load a block whose sum does not match -
which is exactly what a player sees as "Save data is corrupt". Elden Ring slots are not
encrypted, so the data is read by walking the structure in order; several fields have
variable length, which is why the offset of the character coordinates is not known in
advance.

The parsing was carried over from [Erdtree Compass](https://krut.top) and cross-checked
against it: the tests assert that both parsers, on the same files, produce the same
character name, level, map, nearest site of grace and distance to it down to the metre.

---

## Other tools

A survey of what already exists in this niche, and why Erdtree Keeper is built the way it
is, lives in [docs/COMPETITORS.md](docs/COMPETITORS.md).

In short: the tools that gathered an audience have not been updated since 2024 - that is,
since the DLC came out. None of them checks save integrity before restoring, none puts the
location into the file name, and none takes a snapshot on the fact of a write by the game.

## Credits

The reference of sites of grace and boss arenas comes from the
[Erdtree Compass](https://krut.top) project.

Elden Ring and Shadow of the Erdtree are trademarks of FromSoftware and Bandai Namco. This
project is not affiliated with them and contains no game files.

## Changelog

What changed and why, version by version: [CHANGELOG.en.md](CHANGELOG.en.md).

## Licence

MIT - see [LICENSE](LICENSE).
