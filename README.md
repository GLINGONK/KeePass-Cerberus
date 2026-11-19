# KeePass Cerberus

Gestionnaire de mots de passe local pour Windows, **basé sur KeePass 2.62** (Dominik Reichl) et
**100 % compatible avec vos bases KeePass existantes** (format KDBX 4). Aucun serveur, aucun cloud,
aucune télémétrie.

KeePass Cerberus ajoute à KeePass :

| | |
|---|---|
| **Chiffrement en cascade** | 1 à 4 couches parmi AES-256, ChaCha20, Serpent-256, Twofish-256, Camellia-256, clé et nonce indépendants par couche |
| **Profils de sécurité** | Compatible KeePass · Interactif · Renforcé (Argon2id 1 Gio) · **Paranoïa** (AES → ChaCha20 → Serpent, Argon2id 4 Gio) |
| **Facteurs supplémentaires** | PIN, motif sur grille 3×3 à 8×8, partage de secret **Shamir K parmi N** — cumulables avec mot de passe, fichier clé et compte Windows |
| **Anti-capture d'écran** | L'application apparaît en noir dans les captures, enregistrements et partages d'écran (désactivable) |
| **Interface moderne** | WPF, thème sombre / clair, catégories (tags KeePass), panneau de détails, TOTP, générateur |
| **Saisie automatique** | Syntaxe KeePass (`{USERNAME}{TAB}{PASSWORD}{ENTER}`, `{S:Champ}`, `{TOTP}`, `{DELAY n}`…), raccourci global **Ctrl+Alt+A** |
| **QR code** | Identifiant et/ou mot de passe pour un téléphone, fermeture automatique |
| **Interface classique** | L'interface KeePass d'origine reste fournie pour les fonctions avancées |

## Téléchargement

L'installeur Windows est disponible dans les [Releases](../../releases).
Windows 10 / 11 (et 8.1 / 7 SP1 avec .NET Framework 4.8).
L'exécutable n'est pas signé : Windows SmartScreen affichera « éditeur inconnu » au premier lancement.

## Compatibilité

- Toute base KeePass (KDBX 3.1 / 4.x) s'ouvre sans conversion.
- Une base qui utilise une cascade, un profil Cerberus ou des facteurs Cerberus n'est lisible **que** par
  KeePass Cerberus. Le profil « Compatible KeePass » reste lisible par KeePass d'origine.
- Les catégories sont de simples tags KeePass.

## Sécurité

- Cascade : chaque couche est un chiffrement par flot (CTR / ChaCha20) avec clé et nonce dérivés par
  HMAC-SHA-512 ; l'intégrité est assurée par le HMAC de KDBX 4. Une cascade dans un fichier KDBX 3.x
  (non authentifié) est refusée.
- Serpent / Twofish / Camellia : [BouncyCastle](https://www.bouncycastle.org/) (MIT).
- Presse-papiers effacé automatiquement et exclu de l'historique / cloud Windows ; verrouillage
  automatique (inactivité, verrouillage de session, veille) ; ouverture d'URL limitée à http(s).
- **Pas d'audit professionnel.** Revue interne et tests automatisés uniquement.

## Compilation

Prérequis récupérés localement dans `tools\` (non versionnés) :

1. **SDK .NET** (outil de build uniquement) : archive officielle Microsoft extraite dans `tools\dotnet\`.
2. **Assemblys de référence .NET Framework 4.8** : paquet NuGet
   `Microsoft.NETFramework.ReferenceAssemblies.net48` 1.0.3 extrait dans `tools\refasm\`.

Puis :

```bat
:: Interface moderne (WPF)
tools\dotnet\dotnet.exe build src\Cerberus.App -c Release -o build\app

:: Interface classique (KeePass WinForms)
build.bat

:: Installeur (Inno Setup 6), après copie des binaires dans distrib\app
ISCC.exe installer\KeePassCerberus.iss
```

Structure :

```
src/KeePassLib      moteur KeePass (format KDBX, chiffrement, Argon2)
src/Cerberus.Core   moteur + cascade, Shamir, facteurs Cerberus (sans interface)
src/Cerberus.App    interface moderne WPF
src/KeePass         interface KeePass classique (+ ajouts Cerberus dans src/KeePass/Cerberus)
installer/          script Inno Setup
```

## Licence

GNU General Public License v2 ou ultérieure, comme KeePass — voir [LICENSE](LICENSE).
KeePass © Dominik Reichl. BouncyCastle.Cryptography et QRCoder : licence MIT.
