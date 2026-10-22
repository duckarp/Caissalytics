# Contributing to Caissalytics

Thank you for your interest in contributing to Caissalytics! Whether you are an experienced software engineer or a chess enthusiast who loves coding, your contributions are welcome.

---

## 🛠️ Development Setup

### 1. Prerequisites
- **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)** or higher.
- **Git** installed on your system.
- **Linux Packages** (if developing on Linux):
  ```bash
  sudo apt-get install libwebkit2gtk-4.1-dev curl
  ```
- **Recommended IDEs**:
  - [JetBrains Rider](https://www.jetbrains.com/rider/)
  - [Visual Studio Code](https://code.visualstudio.com/) with the C# Dev Kit extension
  - [Visual Studio 2026 / 2024](https://visualstudio.microsoft.com/)

### 2. Clone & Build
```bash
git clone https://github.com/your-username/Caissalytics.git
cd Caissalytics
dotnet build
dotnet test
```

### 3. Running the Desktop Application
```bash
dotnet run --project Caissalytics
```

---

## 📁 Project Structure

```
Caissalytics/
├── Caissalytics/                    # Main Desktop Application (Photino.Blazor)
│   ├── Components/                  # Blazor UI Components
│   │   ├── Analysis/                # Game review, eval chart, engine panels
│   │   ├── Board/                   # Chessground board component & controls
│   │   ├── Dashboard/               # Main dashboard hub & quick actions
│   │   ├── Database/                # SQLite database explorer, filters, PGN import
│   │   ├── Layout/                  # App header, workspace tabs, navigation
│   │   ├── Pages/                   # Home entry point
│   │   ├── Puzzles/                 # Tactics solver & blunder trainer
│   │   ├── Repertoire/              # Opening tree & repertoire editor
│   │   └── Settings/                # Control center, engine config, appearance
│   ├── Core/                        # Pure C# Chess Domain Logic
│   │   ├── BoardPosition.cs         # Board representation & state
│   │   ├── MoveGenerator.cs         # Legal move generator & king safety
│   │   ├── Move.cs                  # Move struct & bitflags
│   │   ├── FenParser.cs             # FEN parser & serializer
│   │   ├── SanParser.cs             # SAN notation parser & generator
│   │   └── PgnReader.cs             # PGN file reader & tokenizer
│   ├── Data/                        # Services, Repositories & REST Clients
│   │   ├── DatabaseManager.cs       # SQLite chess database manager
│   │   ├── OnlineGameSyncService.cs # Lichess & Chess.com sync engine
│   │   ├── RepertoireService.cs     # Personal opening repertoire persistence
│   │   ├── PuzzleService.cs         # Tactics puzzle database service
│   │   ├── AppearanceService.cs     # Board theme & sound manager
│   │   └── UpdateService.cs         # GitHub Releases auto-updater
│   ├── Engine/                      # UCI Chess Engine Management
│   │   ├── EngineManager.cs         # UCI background process management
│   │   └── GameAnalysisService.cs   # Automated game blunder classification
│   └── wwwroot/                     # Web Assets & Interop
│       ├── css/                     # Modular stylesheets (strictly themed)
│       └── js/                      # Chessground & Web Audio synthesizers
├── Caissalytics.Tests/              # xUnit Test Suite (139+ tests)
├── docs/                            # Project Documentation
│   ├── ARCHITECTURE.md              # System design & internals
│   ├── FEATURES.md                  # User manual & features
│   └── CONTRIBUTING.md              # Contributor guidelines
├── publish.sh                       # Linux self-contained build script
└── publish.bat                      # Windows self-contained build script
```

---

## 📋 Coding Standards

### 1. Modern C# (.NET 10 / C# 14)
- Use modern language features where appropriate: primary constructors, collection expressions (`[...]`), pattern matching, `readonly record struct`, and nullable reference types (`#nullable enable`).
- Use asynchronous APIs throughout (`async`/`await`), passing `CancellationToken`s where cancellation is useful.
- Ensure thread safety when manipulating shared caches or files (e.g. `SemaphoreSlim`).

### 2. Strict CSS & UI Guidelines
To maintain pristine UI performance, crisp styling, and clean separation of concerns:
- **ZERO inline styles**: Never use `style="..."` in Razor component markup.
- **ZERO `<style>` tags**: Never embed `<style>` tags inside Razor components.
- **Modular Stylesheets**: Place all styles in the corresponding stylesheet in `Caissalytics/wwwroot/css/` (e.g., `board.css`, `settings.css`, `repertoire.css`).
- **CSS Variables**: Always use system design tokens from `theme.css` for colors, backgrounds, borders, and typography:
  - `var(--bg-base)`, `var(--bg-surface)`, `var(--bg-surface-elevated)`
  - `var(--text-primary)`, `var(--text-muted)`
  - `var(--border-subtle)`, `var(--border-medium)`
  - `var(--accent-blue)`, `var(--accent-green)`, `var(--accent-red)`

### 3. Core Chess Domain Isolation
- Code in `Caissalytics.Core` must remain pure C# with **zero** external UI or framework dependencies.
- Any new move generation or notation parsing rules must include comprehensive unit tests.

### 4. Continuous Documentation Maintenance
- Whenever a feature, UI control, workflow, or configuration option is added or modified, the corresponding documentation **must** be updated as part of the pull request:
  - User-facing capabilities, controls, and workflows -> `docs/FEATURES.md` and `README.md`
  - Internal architecture, service contracts, schema, or system data flow -> `docs/ARCHITECTURE.md`

---

## 🧪 Testing Guidelines

Caissalytics maintains a rigorous test suite in `Caissalytics.Tests`.
Always verify that the entire test suite passes before creating a pull request:

```bash
dotnet test
```

### Adding New Tests
- When implementing a new feature or fixing a bug, write corresponding xUnit unit tests in `Caissalytics.Tests/`.
- Ensure tests run cleanly across both Linux and Windows environments.
- Use isolated temporary directories (`Path.GetTempPath()`) for test services that perform file persistence.

---

## 🔄 Pull Request Workflow

1. **Create a topic branch**:
   ```bash
   git checkout -b feat/my-awesome-feature
   ```
2. **Commit your changes using Conventional Commits**:
   - `feat(module): add new capability`
   - `fix(module): resolve issue with X`
   - `docs(readme): update build instructions`
   - `test(core): add edge case tests for en-passant`
   - `refactor(engine): optimize UCI line parsing`
3. **Verify tests and builds**:
   ```bash
   dotnet test
   ```
4. **Push your branch and open a Pull Request**:
   - Provide a clear summary of your changes.
   - Attach screenshots or recordings for any UI changes.

---

## 🚀 Releasing

Pushing a `v*` tag runs `.github/workflows/release.yml`, which tests, publishes the Linux and Windows archives, **signs them**, and creates the GitHub release.

### Release signing

The in-app updater only installs an update whose archive has a valid detached signature (`<archive>.sig`, ECDSA P-256 / SHA-256), on top of the SHA-256 checksum GitHub publishes. The checksum proves the file matches GitHub's copy; the signature proves it was released with the project's key.

- **Public key**: `release-signing-public-key.pem` in the repository root, also embedded as `UpdateService.ReleaseSigningPublicKeyPem` (a test checks they match).
- **Private key**: stored as the `RELEASE_SIGNING_KEY` secret of the `release` environment (repo *Settings → Environments → release*). Add yourself as a required reviewer there so a tag alone can't use the key.
- **Offline backup**: keep a copy of the private key somewhere safe outside GitHub (password manager or encrypted storage). If it is lost, installed copies can no longer verify updates and users must reinstall manually.

The workflow fails instead of publishing if the secret is missing or doesn't match the committed public key, because the updater would reject such a release.

To check the key setup without releasing, run the workflow manually (*Actions → Release Packages → Run workflow*) with **dry run** ticked (the default): it builds, tests and signs, but publishes nothing.

To sign or check a file by hand:

```bash
openssl dgst -sha256 -sign release-signing-key.pem -out Caissalytics-linux-x64.tar.gz.sig Caissalytics-linux-x64.tar.gz
openssl dgst -sha256 -verify release-signing-public-key.pem -signature Caissalytics-linux-x64.tar.gz.sig Caissalytics-linux-x64.tar.gz
```

**Changing the key**: generate a new pair, ship a release signed with the *old* key that embeds the *new* public key, and only then switch the secret. Releases signed with the new key are accepted only by versions that already contain it.
