# Security

[Русский](SECURITY.md) · **English**

## What the program can and cannot do

Erdtree Keeper runs with ordinary user rights and touches exactly three things:

1. It reads the save folder of the selected game - `%APPDATA%\EldenRing` for Elden Ring,
   `%APPDATA%\DarkSoulsIII` for Dark Souls III and so on; for a game added by hand, the
   folder you picked.
2. It writes copies into the folder you chose.
3. It writes one settings file next to itself.

Plus one action on an explicit command: "Restore to game" overwrites the save file, having
first put the previous one into the `Before restore` subfolder.

### About the network

Since version 1.5.0 the program has an update check, and that is the only thing it reaches
out for. The rules are strict:

- **not a single request goes out without permission.** Permission is asked once, in a
  window of its own, explaining where and what for. Until you answer, the network is not
  touched at all;
- **there is one address and it is in the code**:
  `https://api.github.com/repos/valiDol42/erdtree-keeper/releases/latest`. Links inside the
  server's answer are checked against a list of GitHub hosts - an answer is data, not a
  command, and a tampered one will not make the program download from anywhere else;
- **nothing is sent**: no identifiers, nothing about the machine, nothing about your saves;
- **every request is visible** in the activity log under the `NET` tag, with the full
  address, and the log exports to a text file;
- **what is downloaded is verified** against the checksum from the same release. No match,
  and the file is deleted and the installation never starts. The archive is unpacked only
  after the checksum matches, and paths inside it are checked so that no file can escape
  the folder.

Before 1.5.0 this section said there was no networking code at all, and that could be
checked in the import table. That table now **does** contain `WS2_32.dll`, `CRYPT32.dll`,
`ncrypt.dll` and `IPHLPAPI.DLL`:

```
> dumpbin /dependents ErdtreeKeeper.exe

ADVAPI32.dll        bcrypt.dll          CRYPT32.dll         IPHLPAPI.DLL
KERNEL32.dll        ncrypt.dll          ole32.dll           OLEAUT32.dll
WS2_32.dll          api-ms-win-crt-*.dll
```

What to check now is the behaviour rather than the list of libraries: run TCPView or the
Windows resource monitor and see that the program opens no connection until you press
"Check for updates".

The program does not request administrator rights; that is fixed in the manifest
(`requestedExecutionLevel level="asInvoker"`).

## How to be sure the downloaded file is genuine

**Antivirus scan.** The 1.3.1 build was run through VirusTotal: 69 engines, no
detections - [the report](https://www.virustotal.com/gui/file/9a532a7d6dbe01776179ce20ea0cd0d929a5952c9bb082a03d9f91c521d9bb4f). The report opens by the checksum of the file, so the check
can be repeated independently and should give the same result. It belongs to one specific
build: the next version will have its own checksum and its own report.

In the report the file is listed as `ErdtreeKeeper.dll` - that is the internal .NET assembly
name inside the exe, not a different file.

**Checksum.** Every release page carries a `SHA256SUMS.txt` file. Compare:

```powershell
Get-FileHash .\ErdtreeKeeper.exe -Algorithm SHA256
```

The program computes the same sum itself - in the "About" window.

**Build provenance.** Releases are built by GitHub Actions. For a public repository each
file gets an attestation - a confirmation that it was built from the code in this repository
by that workflow, rather than by someone at home and slipped in under the same name. Check
it with the [GitHub CLI](https://cli.github.com):

```bash
gh attestation verify ErdtreeKeeper.exe --repo <owner>/erdtree-keeper
```

One caveat: GitHub issues no such signature at all for private repositories of personal
accounts ("Feature not available for user-owned private repositories"). While a repository
is closed the check returns an error, and two ways remain - the checksum and a build from
source.

**Your own build.** The most reliable way is to build it yourself:

```bash
dotnet publish src/ErdtreeKeeper/ErdtreeKeeper.csproj -c Release -r win-x64 -o out
```

## Windows warnings

The program has no code signing certificate: those cost money and are issued to legal
entities. So on first launch Windows shows the SmartScreen window, "Windows protected your
PC", with "More info" → "Run anyway".

That message only says the file has no reputation, not that it is malicious. For the same
reason some antivirus products occasionally flag fresh unsigned programs as suspicious -
that is a false positive. If in doubt, build the program yourself: the instructions are
above, and all the code is open.

We will **never** ask you to disable your antivirus or add the program to exclusions. Asking
to switch off protection is a classic sign of malware, and should be treated as such no
matter who is asking.

## Reporting a vulnerability

Open an issue in the repository. If the problem is serious and should not be public right
away, say so in the issue without the details and we will agree on a private channel.

What is of interest first of all:

- Any way to make the program write a file outside the snapshot folder and the game folder.
- Any situation in which a player's save is lost without a backup.
- Any network access without your permission, and any request to an address other than the
  one named above.
- Any way to hand the program an update file whose checksum does not match the published
  one.
