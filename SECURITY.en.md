# Security

[Русский](SECURITY.md) · **English**

## What the program can and cannot do

Erdtree Keeper runs with ordinary user rights and touches exactly three things:

1. It reads the save folder `%APPDATA%\EldenRing`.
2. It writes copies into the folder you chose.
3. It writes one settings file next to itself.

Plus one action on an explicit command: "Restore to game" overwrites the save file, having
first put the previous one into the `Before restore` subfolder.

There is no networking code in the program, and that can be checked on the built file:

```
> dumpbin /dependents ErdtreeKeeper.exe

ADVAPI32.dll        bcrypt.dll          KERNEL32.dll
ole32.dll           OLEAUT32.dll        api-ms-win-crt-*.dll
```

Neither `ws2_32.dll` nor `winhttp.dll` nor `wininet.dll` appears in the imports - without
them a program on Windows cannot open a network connection.

The program does not request administrator rights; that is fixed in the manifest
(`requestedExecutionLevel level="asInvoker"`).

## How to be sure the downloaded file is genuine

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
- Any network access.
