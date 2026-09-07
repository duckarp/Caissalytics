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

## 4. Opponent Preparation & Scouting Dossier 🕵️‍♂️

Prepare for tournament, league, and online matches by generating comprehensive scouting reports on any opponent.

### Player Search & Instant Autocomplete
- Type any opponent's name in the top search bar (e.g. *Kasparov*, *Carlsen*, *Movsesian*).
- As you type, the auto-suggest dropdown pulls matching players directly from your selected SQLite database.
- Choose between searching your **Active Database**, the master **Reference Database**, or **All Databases**.

### On-The-Fly Online Fetching (Lichess & Chess.com)
- Don't have games of your upcoming opponent in your local database?
- Click **🌐 Fetch Online Games** in the toolbar.
- Enter your opponent's handle on Lichess or Chess.com to automatically download their latest game history and immediately generate their dossier!

### Scouting Dossier Overview
The generated report provides instant insights:
- **Rating Profile**: Peak Elo, Average Elo, and latest active rating.
- **Overall & Color Record**: Detailed win / draw / loss breakdown and visual proportion bars for White vs. Black.
- **Playing Style Profile**:
  - *Tactical & Direct*: Decisive games frequently concluding in miniatures under 30 moves.
  - *Endgame Grinder*: High stamina in technical endgames past move 50.
  - *Dynamic Attacker*: Aggressive middlegame play with lower technical endurance.
  - *Solid & Classical*: Balanced, structured play across all game phases.

### Repertoire Scouting & Target Weaknesses
- **White Repertoire (When Opponent is White)**:
  - Categorizes 1st move weapons (`1.e4`, `1.d4`, `1.c4`, `1.Nf3`) by frequency (*Primary Weapon*, *Secondary Line*, *Occasional Choice*).
  - Displays top opening variations, game counts, and scoring percentages.
  - **⚠️ Target Weakness Alert**: Automatically flags lines where the opponent's score drops below 35%, recommending effective theoretical responses.
- **Black Repertoire (When Opponent is Black)**:
  - Details favored defenses against `1.e4`, `1.d4`, and flank systems.
  - **🎯 Recommended Attack Alert**: Pinpoints defenses where the opponent historically struggles, guiding your opening choices when playing White.

### Game Length Tendencies
- Breaks down scoring rates across:
  - Miniatures & Short Games (< 30 moves)
  - Standard Games (30–49 moves)
  - Deep Endgames (50+ moves)

### Games Archive & 1-Click Deep Analysis
- Browse all recorded games for the opponent with result indicators, ECO codes, and move counts.
- Click **Analyze** on any game to immediately launch it into the **Stockfish 18 Analysis Workbench**.
- Click **Export Games** to create a study PGN collection for offline preparation.

---

---

## 5. Syzygy Endgame Tablebases & Classical Endgame Trainer 🏆

Caissalytics integrates full **Syzygy Endgame Tablebases** and an interactive **Classical Endgame Trainer**, empowering players to study and master theoretical endgame positions with exact mathematical certainty.

### Syzygy Tablebase Capabilities
- **$\le 7$-Piece Endgame Coverage**: Exact evaluations for all positions with up to 7 pieces.
- **Online Tablebase Probing**: High-speed, cached queries via the Lichess 7-Piece Tablebase API. No need to store 18+ TB of tablebase files locally.
- **Local Syzygy Path Configuration**: Configure a local folder containing `.rtbw` and `.rtbz` tablebase files under **Control Center → Syzygy Tablebases**. Caissalytics automatically passes `setoption name SyzygyPath` to Stockfish for offline UCI evaluations.
- **Analysis Workbench Integration**: Whenever an analyzed position has 7 or fewer pieces, an interactive **🏆 Tablebase** tab appears in the right panel:
  - Theoretical verdict banner (e.g. `🏆 Winning (DTZ 14 / Mate in 31)`, `🤝 Theoretical Draw`, `⚠️ Losing`).
  - Table of all candidate legal moves with exact category (Win, Draw, Loss), distance to zeroing the 50-move rule (DTZ), and distance to mate (DTM).
  - 1-click move playing directly on the board.

### Classical Endgame Trainer (🏆 Endgames)
The dedicated **Endgame Trainer** features a structured curriculum of 16 essential classical endgame positions across 5 categories:
1. **King & Pawn Endgames**: Key squares, direct opposition, distant opposition, the Trebuchet (mutual zugzwang), triangulation and outflanking, and the rule of the square.
2. **Rook Endgames**: The Lucena position (bridge building), the Philidor defense (3rd-rank cut-off), the Vancura defense (active flank checks against rook pawns), and the short-side defense.
3. **Queen Endgames**: Queen vs 7th-rank center pawn (zigzag technique), Queen vs bishop/rook pawn (stalemate fortresses), and Queen vs Rook (Philidor technique).
4. **Minor Piece Endgames**: Bishop & Knight checkmate (the W maneuver), wrong bishop & rook pawn draw, opposite-colored bishops fortress, and knight vs passed pawns.
5. **Practical Tournament Endgames**: Capablanca's active king principle, Tarrasch's rule (rooks behind passed pawns).

### Interactive Practice & Sparring
- **Target Objectives**: Clear goals for each position (*Win as White*, *Hold Draw as Black*).
- **Theoretical Coaching & Key Squares**: Detailed strategic explanations, coaching advice, and highlighted key squares.
- **Tablebase Sparring Opponent**: When you play your move, the tablebase automatically selects the most stubborn, optimal theoretical defense and replies immediately.
- **Move Quality Evaluation**:
  - 🟢 **Optimal Move**: Preserves the theoretical win or draw.
  - 🟡/🔴 **Blunder Alert**: Immediately warns you if a move drops a theoretical win into a draw or blunder into a loss.
- **Curriculum Mastery Tracking**: Solved positions are marked with ✅ and progress is tracked via the mastery percentage bar.

---

## 6. Tactics & Blunder Workbench 🎯

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

## 7. Online Game Sync & Career Analytics 🌐

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

## 8. Audio Engine & Visual Board Themes 🎨 🔊

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

## 9. Engine Management & Auto-Updater ⚙️

### Custom Chess Engines
- **1-Click Stockfish 18**: Automatically download and configure the latest Stockfish build.
- **Add Custom UCI Engine**: Add any third-party UCI engine (e.g., Leela Chess Zero, Berserk, Komodo) by selecting its executable. Caissalytics automatically runs a probe handshake to verify compatibility.
- **Scan System Engines**: Scans standard OS binary directories (`/usr/bin`, `/usr/local/bin`, `C:\Program Files`) to auto-detect installed engines.

### Auto-Updater
- Caissalytics periodically checks GitHub Releases for new desktop builds.
- When an update is ready, an unobtrusive banner notifies you with release notes and 1-click upgrade options.
