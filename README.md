<div align="center">

# ♞ Caissalytics Desktop

### Modern, High-Performance Chess Analytics & Study Workstation

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512bd4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Photino.Blazor](https://img.shields.io/badge/Photino-Blazor-0078d4.svg?style=flat-square)](https://www.tryphotino.io/)
[![Platform](https://img.shields.io/badge/Platform-Linux%20%7C%20Windows-brightgreen.svg?style=flat-square)](https://github.com/)
[![Tests](https://img.shields.io/badge/Tests-150%20Passing-success.svg?style=flat-square)](file:///home/tomask/projects/Caissalytics/Caissalytics.Tests)
[![License](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)

*Caissalytics is a lightweight, blazing-fast, open-source alternative to ChessBase and commercial chess software. Built with modern .NET 10, Photino.Blazor native desktop shell, Chessground, Stockfish 18, and SQLite.*

</div>

---

## 🌟 Key Highlights

- **⚡ Blazing-Fast Desktop Performance**: Powered by .NET 10 and Photino native OS webviews. Consumes a fraction of the RAM of Electron apps (~60-120 MB vs 600+ MB) and boots instantaneously.
- **💻 Stockfish 18 UCI Analysis**: Multi-PV deep line calculation, real-time evaluation charts, blunder and mistake classification, threat arrows, and 1-click automatic engine installer.
- **🏆 Syzygy Endgame Tablebases**: Instant, cached probing for all positions with $\le 7$ pieces via Lichess 7-Piece Tablebase API with exact WDL and DTZ/DTM metrics. Also supports local `.rtbw`/`.rtbz` tablebase folders for Stockfish search via `SyzygyPath`.
- **👑 Classical Endgame Trainer**: Master theoretical endgames (Lucena bridge, Philidor defense, Vancura active checks, Trebuchet mutual zugzwang, Bishop & Knight mate) against an optimal tablebase-driven sparring opponent with instant blunder warnings.
- **📚 Local & Master Database Engine**: High-throughput SQLite chess indexing engine. Query master games and position stats in sub-millisecond time. Comes with built-in 1-click downloaders for TWIC, Czech & Slovak Extraliga, and custom PGN drag-and-drop.
- **📖 Opening Tree & Repertoire Explorer**: Unified candidate move explorer querying local databases and the live Lichess Masters & Community databases. Build personal White and Black repertoires, attach preparation notes, and export to PGN study files.
- **🕵️‍♂️ Opponent Preparation & Scouting Dossier**: Complete pre-game scouting reports on tournament opponents. Analyzes White & Black opening repertoires, highlights weakest lines, pinpoints tactical vulnerabilities, and classifies playing styles (<30 vs 50+ moves).
- **🎯 Tactics & Personal Blunder Trainer**: Solve curated master puzzles or practice mistakes directly mined from your real online games. Features spaced repetition and tactical explanations.
- **🌐 Online Accounts Sync & Career Analytics**: 1-click historical game sync with Lichess and Chess.com. Interactive rating progress charts, win/draw/loss distributions, and performance breakdown by opening.
- **🎨 Audio Engine & Vector Board Themes**: Real-time synthesized Web Audio acoustic feedback (zero-latency piece thuds, captures, checks, and victory chimes) paired with 6 crisp SVG chessboard themes.
- **🚀 Self-Contained & Auto-Updating**: Clean, independent desktop deployment with integrated GitHub Releases update checker and installer.

---

## 🖥️ Workbench Modules

| Module | Description | Key Capabilities |
|---|---|---|
| **Analysis Workbench** | Master-level game review & position deep-dive | Multi-PV Stockfish 18, dynamic evaluation graph, threat arrows, live reference tree, blunder annotations, Syzygy tablebase panel |
| **Endgame Trainer** | Theoretical endgame curriculum & tablebase sparring | 16 curated positions across 5 categories, automated tablebase defense, move quality evaluation, key square coaching |
| **Database Explorer** | High-volume master & personal game library | SQLite indexing, advanced header/date/ECO filtering, game preview, duplicate detection, batch PGN import |
| **Opening & Repertoire** | Personal opening tree & repertoire builder | Lichess Masters/Community (with free API token support) + 100% offline Local DB stats, move classification (*Main Line*, *Alternative*, *Surprise*), study export |
| **Opponent Preparation** | Comprehensive pre-game opponent scouting dossiers | Repertoire breakdown, target weakness alerts, playing style profile, game length tendencies, 1-click analysis |
| **Tactics Workbench** | Interactive tactical puzzle trainer | Curated master tactics, personalized blunder trainer, dynamic rating system, step-by-step solutions |
| **Career Analytics** | Long-term performance & statistical insight | Elo rating tracking over time, opening win rates, color performance, time-control breakdowns |
| **Sync Center** | Automatic online games aggregator | Direct REST API integration with Lichess & Chess.com, incremental syncing, automatic PGN parsing |
| **Control Center** | Settings, engines & customization hub | Engine management, Syzygy tablebase folder path, online probe toggle, Lichess API token, board themes, sound triggers, updater |

---

## 🏗️ Technical Architecture

Caissalytics combines high-performance compiled C# chess logic with a responsive Blazor UI rendered in native OS WebViews:

```
┌────────────────────────────────────────────────────────┐
│               Photino.Blazor Desktop Shell             │
│  (Native OS Window • WebKitGTK / Edge WebView2 • IPC)   │
└──────────────────────────┬─────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────┐
│                 Blazor Frontend Layer                  │
│   Components/Board (Chessground) • Analysis • Database │
│   Repertoire • Puzzles • Analytics • Settings • Styles │
└─────────────┬───────────────────────────┬──────────────┘
              │                           │
┌─────────────▼───────────────┐ ┌─────────▼──────────────┐
│       Core Chess Domain     │ │   Synthesized WebAudio │
│   BoardPosition • MoveGen   │ │   soundService.js      │
│   FenParser • SanParser     │ │   Vector SVG Themes    │
│   GameTree • ClockHelper    │ │   board-themes.css     │
└─────────────┬───────────────┘ └────────────────────────┘
              │
┌─────────────▼──────────────────────────────────────────┐
│              Services & Storage Engines                │
│  ┌────────────────────┐ ┌───────────────────────────┐  │
│  │   DatabaseManager  │ │       EngineManager       │  │
│  │ SQLite Chess Store │ │ Stockfish 18 UCI Manager  │  │
│  └────────────────────┘ └───────────────────────────┘  │
│  ┌────────────────────┐ ┌───────────────────────────┐  │
│  │ OnlineGameSync     │ │ LichessExplorerClient     │  │
│  │ Lichess & Chess.com│ │ Live Master Opening Stats │  │
│  └────────────────────┘ └───────────────────────────┘  │
│  ┌────────────────────┐ ┌───────────────────────────┐  │
│  │ RepertoireService  │ │ UpdateService             │  │
│  │ Repertoire Store   │ │ GitHub Releases Client    │  │
│  └────────────────────┘ └───────────────────────────┘  │
└────────────────────────────────────────────────────────┘
```

For deeper architectural breakdowns, see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

---

## 🚀 Getting Started

### Prerequisites

- **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)**
- **Operating System Requirements**:
  - **Linux**: `libwebkit2gtk-4.1-dev` (or `libwebkit2gtk-4.0-dev`)
    ```bash
    # Ubuntu / Debian
    sudo apt-get install libwebkit2gtk-4.1-dev curl
    ```
  - **Windows**: Windows 10/11 with WebView2 Runtime (installed by default)

### Build & Run

1. **Clone the repository**:
   ```bash
   git clone https://github.com/your-username/Caissalytics.git
   cd Caissalytics
   ```

2. **Run the desktop app**:
   ```bash
   dotnet run --project Caissalytics
   ```

3. **Run the test suite**:
   ```bash
   dotnet test
   ```

---

## 📦 Building Self-Contained Distributables

Caissalytics includes automated publishing scripts for Linux and Windows that package the application into standalone binaries without requiring the end user to install the .NET runtime:

### On Linux:
```bash
chmod +x publish.sh
./publish.sh
```
*Output: `artifacts/linux-x64/Caissalytics`*

### On Windows:
```cmd
publish.bat
```
*Output: `artifacts\win-x64\Caissalytics.exe`*

---

## 🎨 Board Theming & Acoustics

Caissalytics features an integrated zero-latency sound engine and crisp vector themes:
- **Audio Effects**: Move (wood thud), Capture (impact snap), Check (dual harmonic bell), Victory (major triad chime), and Low Time (clock tick). Fully synthesized via Web Audio API with zero external audio assets.
- **Board Themes**: Classic Walnut, Tournament Green, Ocean Blue, Dark Slate, Cool Marble, and Monochrome.

Customize your theme and audio preferences in **Control Center → Board & Audio**.

---

## 🤝 Contributing

We welcome contributions from chess players and developers! Please read our [docs/CONTRIBUTING.md](docs/CONTRIBUTING.md) guide before submitting pull requests.

---

## 📄 License

Caissalytics is licensed under the [MIT License](LICENSE).
Pieces and board graphics derived from open-source Lichess/Chessground assets.
Stockfish is licensed under the GNU General Public License v3.0.
