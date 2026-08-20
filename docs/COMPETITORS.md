# What already exists in this niche, and how we differ

[Русский](КОНКУРЕНТЫ.md) · **English**

Repository figures were taken through the GitHub API on 12 August 2026. Anything not
verified directly is marked as such.

## Who is here

| Project | Stars | Language | Last commit | Licence | What it does |
| --- | --- | --- | --- | --- | --- |
| [BenGrn/EldenRingSaveCopier](https://github.com/BenGrn/EldenRingSaveCopier) | 550 | C#, WinForms, .NET Framework 4.7.2 | 2024-12-09 | MIT | Moves characters between saves and slots |
| [ClayAmore/ER-Save-Editor](https://github.com/ClayAmore/ER-Save-Editor) | 367 | Rust | 2024-08-13 | Apache-2.0 | A full save editor, Steam ID swapping |
| [Ariescyn/EldenRing-Save-Manager](https://github.com/Ariescyn/EldenRing-Save-Manager) | 268 | Python | 2024-06-17 | none | Backup manager, Steam ID patching, checksum fixing |
| [fyrlin/EldenRingSaveFileManager](https://github.com/wheezyrs/EldenRingSaveFileManager) | 2 | C# | 2025-04-06 | AGPL-3.0 | A console manager, focused on Seamless Co-op |
| [steven-nash/EldenSaves](https://github.com/steven-nash/EldenSaves) | 2 | C# | 2022-05-10 | none | Save copies |
| [Aznlilly/EldenRingSaveBackupTool](https://github.com/Aznlilly/EldenRingSaveBackupTool) | 1 | PowerShell | 2026-07-29 | custom | Backups with rotation, mod support |
| [DeaftJoe/EldenRingCLIBackups](https://github.com/DeaftJoe/EldenRingCLIBackups) | 0 | Python | 2022-03-19 | MIT | Backup and restore from the command line |
| [Ghostbroker/elden-ring-save-converter](https://github.com/Ghostbroker/elden-ring-save-converter) | 0 | Python | 2026-02-04 | none | Moving a save to another Steam ID |
| [mtkennerly/ludusavi](https://github.com/mtkennerly/ludusavi) | 6104 | Rust | 2026-08-10 | MIT | Backs up saves for any game, without knowing the specific one |

### The main conclusion

**Everything in this niche that gathered an audience has been abandoned.** The three leaders
(550, 367 and 268 stars) were last updated in the summer and autumn of 2024. The Shadow of
the Erdtree DLC came out in June 2024 - development stopped exactly when the game changed.

For the leader of the niche this shows in its own issue tracker: the open
["Cant tranfer DLC save"](https://github.com/BenGrn/EldenRingSaveCopier/issues/46) and
["Can't copy seamless coop saves"](https://github.com/BenGrn/EldenRingSaveCopier/issues/41)
are still there. Meanwhile its exe has been downloaded more than a million times (411,312 +
580,450 + 9,052 across three releases) - the demand is enormous and the support is gone.

The living projects are the ones with 0-2 stars. The niche is effectively empty.

## What was worth taking

- **Autosnapshot rotation** (Aznlilly, Ariescyn) - without it the folder fills up in a week.
- **`.co2` support** for Seamless Co-op (fyrlin, Aznlilly) - the mod is popular and needs no
  separate format, but without explicit support the files simply do not show up in the list.
- **A mandatory backup before restoring** (Ariescyn) - without it, overwriting is
  irreversible.
- **Sensible handling of several Steam accounts** - most tools have you pick a single folder,
  and owners of two accounts get confused.

## What nobody has

This is what Erdtree Keeper was written for.

**1. A save integrity check.** Not one tool on the list verifies checksums before putting a
file back into the game. The player learns about the damage when the game says "Save data is
corrupt" - that is, after the progress is already gone. We recompute all 11 MD5 sums and
report per block, and a snapshot known to be broken simply cannot be restored.

**2. A snapshot name taken from the save itself.** Nobody reads the character position. In
every other tool the name is a date and time, or whatever the player typed. Here a button
inserts the nearest site of grace or boss arena, and the DLC tag is added automatically. A
month later "Ellac River Downstream_before" is readable and "backup_2026-08-12_14-05-09" is
not.

**3. A snapshot on the fact of a write, not on a timer.** Every autobackup I have seen runs
on a schedule. The game flushes the save to disk with a delay, so a snapshot on a timer
regularly misses. We wait for the file to change, then wait for it to stop changing, and
only then copy.

**4. A freshness indicator.** Nobody shows whether the game has written the save at all.
This is the main reason a copy turns out to hold the wrong state, and it is solved by one
line in the interface.

**5. Checkable promises instead of assurances.** Tools in this niche are distributed as an
exe from a forum and ask you to take their word for it. Here: the binary imports no
networking library (and CI fails if one appears), a copy is verified by SHA-256, the
activity log shows every access to disk, and the build is confirmed by GitHub attestation. A
dedicated "what the program does and does not do" window exists nowhere else.

**6. A living project.** Given that the whole niche has stood still for two years, that is a
difference in itself.

## What we deliberately do not do

**We do not move saves between Steam accounts and do not move characters between slots.**
BenGrn, ClayAmore, Ariescyn and Ghostbroker can do that, and technically so could we: the
file layout is worked out, the Steam ID sits in four places, and the checksum algorithm has
been verified against real files.

But any such feature means the program rewrites the contents of a save. And the position "we
do not change a single byte inside the save" is the strongest thing you can say to a
suspicious audience - worth more than another tick in a feature list. Anyone who needs the
transfer will take an editor; there it is the main job, not a side one.

**We do not build a universal backup tool for all games.** That is Ludusavi's niche, and it
does it better. Our advantage is knowing Elden Ring specifically: the checksums, the
character position, the location names, the way the game behaves when writing. A universal
tool will never have that.

**We do not edit items, levels or flags.** That is a save editor - a different program, with
a different audience and different risks.
