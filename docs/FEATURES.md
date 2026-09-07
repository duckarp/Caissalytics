# Caissalytics User & Feature Manual

Welcome to the comprehensive user guide for **Caissalytics Desktop**. This manual describes every workbench module, configuration option, and workflow available in the application.

---

## 1. Analysis Workbench 🔬

The Analysis Workbench is your central station for exploring positions, preparing openings, and reviewing completed games with grandmaster-level engine insight.

### Board Navigation & Interaction
- **Making Moves**: Click on a piece and click the destination square, or drag-and-drop. Valid target destinations are highlighted in real-time.
- **Pawn Promotion**: When promoting a pawn, a prompt appears allowing selection of Queen, Rook, Bishop, or Knight.
- **Flipping the Board**: Click the Flip icon in the board toolbar to view the position from Black's perspective.
- **Keyboard & Button Navigation**:
  - `|◀` (First): Return to the starting position.
  - `◀` (Previous): Step back one ply.
  - `▶` (Next): Step forward one ply along the mainline.
  - `▶|` (Last): Jump to the end of the mainline.

### Stockfish 18 Engine & Evaluation
- **Toggle Engine**: Click **Toggle Engine** in the engine control panel to start or halt evaluation.
- **Multi-PV Analysis**: View up to 5 principal variation lines simultaneously. Switch between candidate moves to explore secondary defenses.
- **Threat Detection**: Visual threat arrows on the chessboard illustrate impending tactical shots or tactical sequences.
- **Evaluation Gauge**: The vertical side bar provides continuous visual feedback on position advantage ($+3.50$, $-1.20$, or $\#4$ for forced mate).

### Full Game Review & Blunder Classification
- Click **Analyze Game** to run a comprehensive batch analysis of every move in the game.
- The interactive **Evaluation Chart** graphs advantage progression across all moves.
- Moves are classified with standard chess symbols:
  - 🟢 **Best Move / Book**: Engine top choice.
  - 🔵 **Brilliant / Great**: Exceptional tactical find.
  - 🟡 **Inaccuracy**: Minor sub-optimal choice.
  - 🟠 **Mistake**: Measurable loss in advantage.
  - 🔴 **Blunder**: Critical oversight that alters the game trajectory.
- **Accuracy Score**: Summary percentage (e.g. 94.2% White vs 88.1% Black).

---

## 2. Database Explorer 🗄️

The Database Explorer provides high-performance storage and searching for personal and master-level chess games.

### Database Operations
- **Active Database Switcher**: Switch between databases using the dropdown menu.
- **Create New Database**: Create clean, independent SQLite databases for specific tournaments, study topics, or historical archives.
- **Drag-and-Drop PGN Import**: Drag any `.pgn` file onto the window to automatically parse and index all games.
- **Duplicate Detection**: The importer inspects players, date, event, and moves to prevent identical games from clogging your database.

### 1-Click Database Installers
From the **Database Hub**, download and install pre-compiled databases with a single click:
- **Czech & Slovak Extraliga & Leagues** (140,000+ games): Master and league matches from Slovakia and the Czech Republic from 1990 to the present.
- **The Week In Chess (TWIC) Aggregation**: Hundreds of thousands of top contemporary grandmaster games.

### Searching & Filtering
Filter master databases instantly by:
- **Player Names**: White or Black player name search (supports partial matching).
- **Date & Years**: Narrow games by year ranges (e.g. `2020 - 2026`).
- **ECO Codes**: Filter by ECO opening code (e.g., `B90` for Sicilian Najdorf).
- **Game Result**: Filter by White wins (`1-0`), Draws (`1/2-1/2`), or Black wins (`0-1`).

---

## 3. Opening Tree & Personal Repertoire Explorer 📖

Understand what moves are played at the master level and build a bulletproof opening preparation repertoire.

### Opening Candidate Statistics
From any board position, the Candidate Moves Table displays:
- **Move SAN**: e.g., `1. e4`, `c5`, `Nf3`.
- **Game Count & Frequency %**: Percentage of games choosing this move from the current position.
- **Score %**: White's expected win rate ($W\% + 0.5 \times D\%$).
- **Win / Draw / Loss Bar**: Visual breakdown of results.
- **Average Elo**: Average rating of players selecting this line.
- **Years**: Historical span when this move was played (e.g., `1995 - 2026`).

### Live Sources
Switch between statistical sources seamlessly:
1. **Local Database**: Queries your currently selected SQLite database.
2. **Lichess Masters API**: Real-time stats from over 25 million FIDE over-the-board master games (Elo 2200+).
3. **Lichess Community API**: Opening statistics across millions of online rated games.

### Repertoire Builder & Study Notes
- **Star Bookmark**: Click the Star icon on any candidate move to add it to your personal repertoire.
- **Line Categorization**:
  - **Main Line**: Your primary chosen answer.
  - **Alternative**: Secondary branch for variety or specific opponents.
  - **Surprise Weapon**: Sharp, tactical sideline designed to test opponent preparation.
- **Preparation Notes**: Add personal study explanations, warning traps, and memory cues.
- **PGN Export**: Click **Export Repertoire to PGN** to generate a study file compatible with ChessBase, Lichess Studies, or PGN viewers.

---

## 4. Tactics & Blunder Workbench 🎯

Improve your tactical vision and eliminate recurring game mistakes.

### Modes & Filters
- **All Tactics**: Practice curated tactical puzzles across all themes (pins, skewers, discovered checks, deflections).
- **My Blunders**: Solve puzzles extracted directly from your own lost or blunder-ridden games synced from Lichess and Chess.com!
- **Master Tactics**: Classical tactical compositions from historical grandmaster encounters.
- **Review**: Re-attempt previously missed puzzles to reinforce pattern recognition.

### Dynamic Solving Experience
- The board automatically plays the opponent's setup move.
- Find the best continuation; if correct, the board replies with the opponent's defense until the tactical sequence concludes.
- Dynamic rating adjustment: Earn rating points upon correct solution; review step-by-step explanations on failed attempts.
- Victory celebratory acoustic chime upon solving.

---

## 5. Online Game Sync & Career Analytics 🌐

Connect your online identities to track progress and study personal trends.

### Connecting Online Accounts
Under **Control Center → Profile & Handles**:
- Enter your **Lichess Username** and **Chess.com Username**.
- Enter your official **FIDE ID** (with direct links to your ratings card).

### 1-Click Game Sync
- In the **Online Sync** panel, click **Sync All Games**.
- Caissalytics incrementally queries the Lichess and Chess.com APIs, downloading newly played games and parsing them directly into your local database.

### Career Performance Analytics
- **Rating History Curves**: Visualize your Elo rating trajectory across Blitz, Rapid, and Classical formats.
- **Win / Draw / Loss Distribution**: Inspect your performance as White vs. Black.
- **Opening Performance Matrix**: Discover which openings yield your highest winning percentages and which result in blunders.

---

## 6. Audio Engine & Visual Board Themes 🎨 🔊

### Synthesized Web Audio Engine
Caissalytics features an integrated zero-latency sound engine that synthesizes realistic acoustic feedback via the Web Audio API without downloading audio files:
- **Move**: Tactile wood contact thud.
- **Capture**: Crisp mechanical impact snap.
- **Check**: Dual harmonic alert bell.
- **Victory**: Ascending major triad celebration chime.
- **Low Time Warning**: Fast wooden clock tick.

In **Control Center → Board & Audio**:
- Adjust the **Master Volume** slider from 0% to 100%.
- Toggle individual sound triggers on or off.
- Click **▶ Test** to preview any sound effect immediately.

### Vector Chessboard Themes
Choose between 6 vector board styles rendered via crisp SVGs that never blur:
1. **Classic Walnut (`brown`)**: Traditional wooden board aesthetic.
2. **Tournament Green (`green`)**: FIDE standard tournament green and buff.
3. **Ocean Blue (`blue`)**: Calming azure contrast.
4. **Dark Slate (`slate`)**: Charcoal modern slate.
5. **Cool Marble (`marble`)**: Frost-textured cool stone.
6. **Monochrome (`monochrome`)**: High-contrast vintage newspaper print.

---

## 7. Engine Management & Auto-Updater ⚙️

### Custom Chess Engines
- **1-Click Stockfish 18**: Automatically download and configure the latest Stockfish build.
- **Add Custom UCI Engine**: Add any third-party UCI engine (e.g., Leela Chess Zero, Berserk, Komodo) by selecting its executable. Caissalytics automatically runs a probe handshake to verify compatibility.
- **Scan System Engines**: Scans standard OS binary directories (`/usr/bin`, `/usr/local/bin`, `C:\Program Files`) to auto-detect installed engines.

### Auto-Updater
- Caissalytics periodically checks GitHub Releases for new desktop builds.
- When an update is ready, an unobtrusive banner notifies you with release notes and 1-click upgrade options.
