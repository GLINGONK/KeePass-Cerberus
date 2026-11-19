# KeePass Cerberus

A local password manager for Windows, **based on KeePass 2.62** (Dominik Reichl) and
**fully compatible with your existing KeePass databases** (KDBX 4 format). No server, no cloud,
no telemetry.

KeePass Cerberus adds to KeePass:

| | |
|---|---|
| **Cascade encryption** | 1 to 4 layers among AES-256, ChaCha20, Serpent-256, Twofish-256, Camellia-256, with an independent key and nonce per layer |
| **Security profiles** | KeePass-compatible · Interactive · Hardened (Argon2id 1 GiB) · **Paranoid** (AES → ChaCha20 → Serpent, Argon2id 4 GiB) |
| **Extra factors** | PIN, pattern on a 3×3 to 8×8 grid, **Shamir K-of-N** secret sharing — stackable with master password, key file and Windows account |
| **Screen-capture protection** | The application shows up black in screenshots, recordings and screen sharing (can be turned off) |
| **Modern interface** | WPF, dark / light theme, categories (KeePass tags), details pane, TOTP, password generator |
| **Auto-type** | KeePass syntax (`{USERNAME}{TAB}{PASSWORD}{ENTER}`, `{S:Field}`, `{TOTP}`, `{DELAY n}`…), global hotkey **Ctrl+Alt+A** |
| **QR code** | Username and/or password for a phone, closes automatically |
| **Classic interface** | The original KeePass interface is still included for advanced features |

## Download

The Windows installer is available under [Releases](../../releases).
Windows 10 / 11 (and 8.1 / 7 SP1 with .NET Framework 4.8).
The executable is not code-signed: Windows SmartScreen will show "unknown publisher" on first launch.

## Compatibility

- Any KeePass database (KDBX 3.1 / 4.x) opens without conversion.
- A database that uses a cascade, a Cerberus profile or Cerberus factors can **only** be opened by
  KeePass Cerberus. The "KeePass-compatible" profile remains readable by the original KeePass.
- Categories are plain KeePass tags.

## Security

- Cascade: each layer is a stream cipher (CTR / ChaCha20) with a key and nonce derived via
  HMAC-SHA-512; integrity is provided by the KDBX 4 HMAC. A cascade inside a KDBX 3.x file
  (unauthenticated) is rejected.
- Serpent / Twofish / Camellia: [BouncyCastle](https://www.bouncycastle.org/) (MIT).
- Clipboard auto-cleared and excluded from Windows clipboard history / cloud; automatic lock
  (idle, session lock, sleep); URL opening restricted to http(s).
- **No professional audit.** Internal review and automated tests only.

## Building

Prerequisites fetched locally into `tools\` (not versioned):

1. **.NET SDK** (build tool only): official Microsoft archive extracted into `tools\dotnet\`.
2. **.NET Framework 4.8 reference assemblies**: NuGet package
   `Microsoft.NETFramework.ReferenceAssemblies.net48` 1.0.3 extracted into `tools\refasm\`.

Then:

```bat
:: Modern interface (WPF)
tools\dotnet\dotnet.exe build src\Cerberus.App -c Release -o build\app

:: Classic interface (KeePass WinForms)
build.bat

:: Installer (Inno Setup 6), after copying the binaries into distrib\app
ISCC.exe installer\KeePassCerberus.iss
```

Layout:

```
src/KeePassLib      KeePass engine (KDBX format, encryption, Argon2)
src/Cerberus.Core   engine + cascade, Shamir, Cerberus factors (no UI)
src/Cerberus.App    modern WPF interface
src/KeePass         classic KeePass interface (+ Cerberus additions in src/KeePass/Cerberus)
installer/          Inno Setup script
```

## License

GNU General Public License v2 or later, like KeePass — see [LICENSE](LICENSE).
KeePass © Dominik Reichl. BouncyCastle.Cryptography and QRCoder: MIT license.
