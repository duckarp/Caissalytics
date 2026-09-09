using System.Collections.Concurrent;
using Caissalytics.Data;

namespace Caissalytics.Localization;

public class LocalizationService : ILocalizationService
{
    private readonly IAppearanceService? _appearanceService;
    private string _currentLanguage = "en";
    private readonly ConcurrentDictionary<string, LanguageInfo> _languages = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _dictionaries = new(StringComparer.OrdinalIgnoreCase);

    public event Action? OnLanguageChanged;

    public string CurrentLanguage => _currentLanguage;

    public IReadOnlyList<LanguageInfo> SupportedLanguages =>
        _languages.Values.OrderBy(l => l.Code == "en" ? 0 : l.Code == "sk" ? 1 : 2).ThenBy(l => l.Name).ToList();

    public LocalizationService(IAppearanceService? appearanceService = null, bool loadPersistedSettings = true)
    {
        _appearanceService = appearanceService;

        // Register default languages
        RegisterBuiltInLanguages();

        if (loadPersistedSettings)
        {
            // 1. Fast synchronous check of appearance_settings.json for instant startup without flicker
            try
            {
                string configDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Caissalytics");
                string settingsPath = Path.Combine(configDir, "appearance_settings.json");
                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Language", out var langProp) ||
                        doc.RootElement.TryGetProperty("language", out langProp))
                    {
                        string? lang = langProp.GetString();
                        if (!string.IsNullOrWhiteSpace(lang) && _languages.ContainsKey(lang))
                        {
                            _currentLanguage = lang;
                        }
                    }
                }
            }
            catch { }

            // 2. Load language preference from appearanceService if available
            if (_appearanceService != null)
            {
                try
                {
                    var settingsTask = _appearanceService.GetSettingsAsync();
                    if (settingsTask.IsCompletedSuccessfully)
                    {
                        var lang = settingsTask.Result.Language;
                        if (!string.IsNullOrWhiteSpace(lang) && _languages.ContainsKey(lang))
                        {
                            _currentLanguage = lang;
                        }
                    }
                    else
                    {
                        _ = settingsTask.ContinueWith(t =>
                        {
                            if (t.IsCompletedSuccessfully && !string.IsNullOrWhiteSpace(t.Result.Language) && _languages.ContainsKey(t.Result.Language))
                            {
                                if (_currentLanguage != t.Result.Language)
                                {
                                    _currentLanguage = t.Result.Language;
                                    OnLanguageChanged?.Invoke();
                                }
                            }
                        });
                    }
                }
                catch { }
            }
        }
    }

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            // 1. Try active language
            if (_dictionaries.TryGetValue(_currentLanguage, out var dict) && dict.TryGetValue(key, out var val))
            {
                return val;
            }

            // 2. Fallback to English
            if (_currentLanguage != "en" && _dictionaries.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var enVal))
            {
                return enVal;
            }

            // 3. Fallback to key itself
            return key;
        }
    }

    public string this[string key, params object[] args]
    {
        get
        {
            string template = this[key];
            if (args == null || args.Length == 0) return template;
            try
            {
                return string.Format(template, args);
            }
            catch
            {
                return template;
            }
        }
    }

    public async Task SetLanguageAsync(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode) || !_languages.ContainsKey(languageCode))
        {
            return;
        }

        if (_currentLanguage == languageCode)
        {
            return;
        }

        _currentLanguage = languageCode;

        if (_appearanceService != null)
        {
            try
            {
                var settings = await _appearanceService.GetSettingsAsync();
                settings.Language = languageCode;
                await _appearanceService.SaveSettingsAsync(settings);
            }
            catch { }
        }

        OnLanguageChanged?.Invoke();
    }

    public void RegisterLanguage(LanguageInfo info, IReadOnlyDictionary<string, string> strings)
    {
        _languages[info.Code] = info;
        var dict = _dictionaries.GetOrAdd(info.Code, _ => new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        foreach (var (k, v) in strings)
        {
            dict[k] = v;
        }
    }

    private void RegisterBuiltInLanguages()
    {
        // 🇬🇧 English
        RegisterLanguage(new LanguageInfo("en", "English", "English", "🇬🇧"), GetEnglishStrings());

        // 🇸🇰 Slovak
        RegisterLanguage(new LanguageInfo("sk", "Slovak", "Slovenčina", "🇸🇰"), GetSlovakStrings());
    }

    private static Dictionary<string, string> GetEnglishStrings() => new(StringComparer.OrdinalIgnoreCase)
    {
        // App Navigation & Header
        ["Nav_Brand"] = "Caissalytics",
        ["Nav_Insights"] = "Insights",
        ["Nav_ControlCenter"] = "Control Center",
        ["Nav_NewBoard"] = "+ New Board",
        ["Nav_Database"] = "Database",
        ["Nav_Update"] = "Update v{0}",

        // Dashboard Hub
        ["Dash_Welcome"] = "Welcome to Caissalytics",
        ["Dash_Subtitle"] = "A modern, open-source chess analysis and database platform.",
        ["Dash_Card_Analysis"] = "New Analysis Board",
        ["Dash_Card_Analysis_Desc"] = "Open an interactive board to explore moves, build variations, annotate with glyphs, and run Stockfish.",
        ["Dash_Card_Databases"] = "Browse Databases",
        ["Dash_Card_Databases_Desc"] = "Manage collections, search games by player, ECO, or Elo, and import your PGN libraries.",
        ["Dash_Card_OpenPgn"] = "Open PGN File",
        ["Dash_Card_OpenPgn_Desc"] = "Import a game or study from your computer. Supports full notation, branching variations, and annotations.",
        ["Dash_Card_OnlineGames"] = "My Online Games",
        ["Dash_Card_OnlineGames_Desc"] = "Sync and analyze your games from Lichess and Chess.com into your local database.",
        ["Dash_Card_Puzzles"] = "Solve My Blunders",
        ["Dash_Card_Puzzles_Desc"] = "Practice tactical positions and blunder recovery from your real games and master combinations.",
        ["Dash_Card_Repertoire"] = "Opening Tree & Repertoire",
        ["Dash_Card_Repertoire_Desc"] = "Explore move frequency, win rates, and master games from your databases and build your personal repertoire.",
        ["Dash_Card_Dossier"] = "Opponent Preparation",
        ["Dash_Card_Dossier_Desc"] = "Generate comprehensive scouting dossiers on tournament opponents, analyze their opening repertoire, and expose weaknesses.",
        ["Dash_Card_Endgames"] = "Endgame Trainer",
        ["Dash_Card_Endgames_Desc"] = "Master theoretical endgames against Stockfish with instant Syzygy tablebase verification and step-by-step goals.",
        ["Dash_Card_Insights_Desc"] = "Explore your win/loss performance, White vs Black stats, rating timeline, and opening repertoire.",
        ["Dash_Card_Homework"] = "Printable Homework & Diagrams",
        ["Dash_Card_Homework_Desc"] = "Create and print customized chess worksheets with diagrams, student header, exercise prompts, and answer keys.",
        ["Dash_Card_Settings"] = "Control Center & Settings",
        ["Dash_Card_Settings_Desc"] = "Manage chess engines, Syzygy tablebase paths, user profile, account synchronization, and board themes.",

        // Settings Navigation Categories
        ["Settings_Title"] = "Settings & Control Center",
        ["Settings_Subtitle"] = "Configure your player profile, chess engines, databases, and visual preferences.",
        ["Settings_Nav_Profile"] = "Profile & Handles",
        ["Settings_Nav_Profile_Desc"] = "Name, FIDE ID, online accounts",
        ["Settings_Nav_Appearance"] = "Interface, Board & Audio",
        ["Settings_Nav_Appearance_Desc"] = "Language, themes, sound effects, volume",
        ["Settings_Nav_Engines"] = "Chess Engines",
        ["Settings_Nav_Engines_Desc"] = "Installed engines, active UCI, 1-click install",
        ["Settings_Nav_Tablebases"] = "Syzygy Tablebases",
        ["Settings_Nav_Tablebases_Desc"] = "Endgame probe, local path, online API",
        ["Settings_Nav_Sync"] = "Online Games Sync",
        ["Settings_Nav_Sync_Desc"] = "Batch size, database status, auto sync",
        ["Settings_Nav_Updates"] = "App & Updates",
        ["Settings_Nav_Updates_Desc"] = "Version, releases, auto-updater",

        // Language Selection Section
        ["Settings_Lang_Title"] = "Application Language",
        ["Settings_Lang_Desc"] = "Select your preferred language. Changes take effect immediately across all screens.",
        ["Settings_Lang_Active"] = "Active",

        // Appearance / Board Themes
        ["Settings_BoardThemes_Title"] = "Visual Board Themes",
        ["Settings_BoardThemes_Desc"] = "Select your preferred vector chessboard styling. Crisp rendering at all display resolutions.",
        ["Settings_SoundEngine_Title"] = "Chess Sound Engine",
        ["Settings_SoundEngine_Desc"] = "Crisp audio effects with native system output.",
        ["Settings_Sound_Master"] = "Enable sound effects",
        ["Settings_Sound_Volume"] = "Sound Volume",
        ["Settings_Sound_Move"] = "Piece Move",
        ["Settings_Sound_Capture"] = "Capture",
        ["Settings_Sound_Check"] = "Check",
        ["Settings_Sound_Victory"] = "Victory & Checkmate",
        ["Settings_Sound_LowTime"] = "Low Time Warning",
        ["Settings_Sound_Test"] = "Test",

        // Common Actions
        ["Common_Save"] = "Save Settings",
        ["Common_Cancel"] = "Cancel",
        ["Common_Delete"] = "Delete",
        ["Common_Remove"] = "Remove",
        ["Common_Active"] = "Active",
        ["Common_Installed"] = "Installed",
        ["Common_Update"] = "Update",
        ["Common_Close"] = "Close",
        ["Common_MoveUp"] = "Move Up",
        ["Common_MoveDown"] = "Move Down",
        ["Common_PreferencesSaved"] = "✓ Preferences saved!",

        // Engine Removal Dialog
        // Engine Removal Dialog
        ["Engine_RemoveConfirm_Title"] = "Remove Chess Engine",
        ["Engine_RemoveConfirm_Warning"] = "Confirm Engine Removal",
        ["Engine_RemoveConfirm_Question"] = "Are you sure you want to remove {0}?",
        ["Engine_RemoveConfirm_ActiveWarning"] = "This engine is currently set as your Active Engine. If removed, Caissalytics will automatically switch to a remaining installed engine.",
        ["Engine_RemoveConfirm_Btn"] = "Remove Engine",

        // Workspace Tabs
        ["Tab_Dashboard"] = "Dashboard",
        ["Tab_Analysis"] = "Analysis Board",
        ["Tab_Database"] = "Database",
        ["Tab_Settings"] = "Control Center",
        ["Tab_Analytics"] = "Personal Insights",
        ["Tab_Puzzles"] = "Puzzle Trainer",
        ["Tab_Repertoire"] = "Opening Tree",
        ["Tab_OpponentDossier"] = "Opponent Prep",
        ["Tab_Endgames"] = "Endgames",
        ["Tab_Homework"] = "Homework & Diagrams",

        // Analysis Workbench
        ["Analysis_Tab_Moves"] = "Moves & Annotations",
        ["Analysis_Tab_Report"] = "Game Report",
        ["Analysis_Tab_Reference"] = "Opening Reference",
        ["Analysis_Tab_Tablebase"] = "🏆 Tablebase",
        ["Analysis_Tab_Tools"] = "PGN & FEN",
        ["Analysis_SaveToDb"] = "💾 Save to DB",
        ["Analysis_SaveGameToDb"] = "💾 Save Game to Database",
        ["Analysis_GameHeaders"] = "Game Headers",
        ["Analysis_White"] = "White:",
        ["Analysis_Black"] = "Black:",
        ["Analysis_Event"] = "Event:",
        ["Analysis_Result"] = "Result:",
        ["Analysis_Fen"] = "FEN",
        ["Analysis_PgnExport"] = "PGN Export",
        ["Analysis_CopyFen"] = "📋 Copy FEN",
        ["Analysis_CopyPgn"] = "📋 Copy PGN",
        ["Analysis_Copied"] = "✅ Copied!",
        ["Analysis_CasualAnalysis"] = "Casual Analysis",
        ["Analysis_AddComment"] = "Add a comment to this move...",
        ["Analysis_SaveComment"] = "Save",
        ["Analysis_PromoteVariation"] = "⬆ Main",
        ["Analysis_DeleteVariation"] = "Delete Variation",
        ["Analysis_ClearGlyphs"] = "Clear Glyphs",

        // Engine Analysis Panel
        ["Engine_Stop"] = "⏹ Stop",
        ["Engine_Start"] = "▶ Start",
        ["Engine_Depth"] = "depth",
        ["Engine_Nps"] = "nps",
        ["Engine_Idle"] = "Engine Idle",
        ["Engine_Line_1"] = "1 Line",
        ["Engine_Line_2"] = "2 Lines",
        ["Engine_Line_3"] = "3 Lines",
        ["Engine_Line_4"] = "4 Lines",
        ["Engine_Line_5"] = "5 Lines",
        ["Engine_AddVariation"] = "+ Variation",
        ["Engine_NotInstalled"] = "Chess Engine is not installed yet",
        ["Engine_NotInstalledDesc"] = "Enable 1-click deep engine analysis, live eval bar, and best-move arrows.",
        ["Engine_Installing"] = "Downloading and unpacking official binary ({0}%)...",
        ["Engine_Install1Click"] = "⚡ 1-Click Install",
        ["Engine_ClickStart"] = "Click ▶ Start to evaluate candidate moves with Stockfish",
        ["Engine_Evaluating"] = "Evaluating position...",
        ["Engine_UpdatingStockfish"] = "Updating ({0}%)...",
        ["Engine_UpdateStockfishBtn"] = "⚡ Update to {0}",

        // Game Report Panel
        ["Report_FullGameAnalysis"] = "Full Game Analysis",
        ["Report_FullGameDesc"] = "Run Stockfish through every move of the game to generate player accuracy scores, detect blunders & mistakes, and build the interactive evaluation curve.",
        ["Report_QuickScan"] = "Quick Scan (80ms)",
        ["Report_Standard"] = "Standard (150ms)",
        ["Report_Deep"] = "Deep Analysis (400ms)",
        ["Report_AnalyzeEntireGame"] = "📊 Analyze Entire Game",
        ["Report_AnalyzingWithStockfish"] = "Analyzing game with Stockfish...",
        ["Report_MoveProgress"] = "Move {0} of {1} ({2})",
        ["Report_Classification"] = "Move Classification Breakdown",
        ["Report_Best"] = "Best",
        ["Report_Excellent"] = "Excellent",
        ["Report_Good"] = "Good",
        ["Report_Inaccuracy"] = "Inaccuracy (?!)",
        ["Report_Mistake"] = "Mistake (?)",
        ["Report_Blunder"] = "Blunder (??)",
        ["Report_AnnotateTree"] = "✨ Annotate Move Tree",
        ["Report_TreeAnnotated"] = "✓ Tree Annotated",
        ["Report_NextMistake"] = "🔍 Next Mistake ▶",

        // Tablebase Panel
        ["Tablebase_Title"] = "Syzygy Tablebase",
        ["Tablebase_Subheading"] = "{0} pieces on board • 7-Piece Syzygy",
        ["Tablebase_Refresh"] = "↻ Refresh",
        ["Tablebase_Probing"] = "Probing Syzygy Tablebase...",
        ["Tablebase_ExactMate"] = "Exact Mate: {0} plies",
        ["Tablebase_DistanceToZero"] = "Distance to Zero (DTZ): {0} plies",
        ["Tablebase_Checkmate"] = "Checkmate",
        ["Tablebase_Stalemate"] = "Stalemate",
        ["Tablebase_Move"] = "Move",
        ["Tablebase_Result"] = "Result",
        ["Tablebase_Dtz"] = "DTZ",
        ["Tablebase_Dtm"] = "DTM",
        ["Tablebase_Action"] = "Action",

        // Save Game Modal
        ["SaveGame_Title"] = "💾 Save Game to Database",
        ["SaveGame_TargetDb"] = "Target Database",
        ["SaveGame_SaveMode"] = "Save Mode",
        ["SaveGame_Overwrite"] = "Overwrite existing record (#{0})",
        ["SaveGame_OverwriteDesc"] = "Updates the existing game and its positions in {0}",
        ["SaveGame_SaveAsNew"] = "Save as a new game record",
        ["SaveGame_SaveAsNewDesc"] = "Leaves original game #{0} untouched and creates a new entry",
        ["SaveGame_WhitePlayer"] = "White Player",
        ["SaveGame_WhiteElo"] = "White Elo",
        ["SaveGame_BlackPlayer"] = "Black Player",
        ["SaveGame_BlackElo"] = "Black Elo",
        ["SaveGame_Event"] = "Event / Tournament",
        ["SaveGame_Site"] = "Site / Location",
        ["SaveGame_Round"] = "Round",
        ["SaveGame_Date"] = "Date",
        ["SaveGame_Result"] = "Result",
        ["SaveGame_Eco"] = "ECO Code",
        ["SaveGame_Saving"] = "Saving...",
        ["SaveGame_BtnSave"] = "Save Game",

        // Database Browser
        ["Db_Databases"] = "Databases",
        ["Db_NewDb"] = "+ New DB",
        ["Db_Reference"] = "⭐ Reference",
        ["Db_ReferenceDb"] = "⭐ Reference DB",
        ["Db_SetRef"] = "⭐ Set Ref",
        ["Db_SetAsReference"] = "⭐ Set as Reference",
        ["Db_Sync"] = "🔄 Sync",
        ["Db_SyncGames"] = "🔄 Sync games",
        ["Db_Delete"] = "🗑️ Delete",
        ["Db_MasterLib"] = "📚 Master Library",
        ["Db_AddNewGame"] = "+ Add a new game",
        ["Db_ImportPgn"] = "📥 Import PGN",
        ["Db_CleanForeign"] = "🧹 Clean foreign games",
        ["Db_GamesCount"] = "{0} games",
        ["Db_Filter_Player"] = "Player",
        ["Db_Filter_PlayerPlaceholder"] = "e.g. Kasparov, Fischer",
        ["Db_Filter_Eco"] = "ECO",
        ["Db_Filter_MinElo"] = "Min Elo",
        ["Db_Filter_Result"] = "Result",
        ["Db_Filter_All"] = "All",
        ["Db_Filter_WhiteWin"] = "1-0 (White win)",
        ["Db_Filter_BlackWin"] = "0-1 (Black win)",
        ["Db_Filter_Draw"] = "½-½ (Draw)",
        ["Db_Filter_Event"] = "Event",
        ["Db_Filter_EventPlaceholder"] = "Tournament / Event",
        ["Db_Filter_Search"] = "Search",
        ["Db_Filter_Reset"] = "Reset",
        ["Db_Col_Site"] = "Site",
        ["Db_Col_White"] = "White",
        ["Db_Col_Black"] = "Black",
        ["Db_Col_Elo"] = "Elo",
        ["Db_Col_Result"] = "Result",
        ["Db_Col_Eco"] = "ECO",
        ["Db_Col_Date"] = "Date",
        ["Db_Col_Event"] = "Event",
        ["Db_Col_Moves"] = "Moves",
        ["Db_OpenGame"] = "Open",
        ["Db_DeleteGame"] = "Delete Game",
        ["Db_ShowingGames"] = "Showing {0} - {1} of {2} games",
        ["Db_PrevPage"] = "◀ Prev",
        ["Db_NextPage"] = "Next ▶",
        ["Db_PageOf"] = "Page {0} of {1}",
        ["Db_LoadingGames"] = "Loading games...",
        ["Db_NoGamesMatch"] = "No games match your search criteria.",
        ["Db_NoGamesMatchDesc"] = "Try clearing filters or importing games from PGN files.",
        ["Db_CreateDb_Title"] = "Create New Database",
        ["Db_CreateDb_Name"] = "Database Name",
        ["Db_CreateDb_Placeholder"] = "e.g. Masters, Sicilian, LichessBlitz",
        ["Db_CreateDb_Btn"] = "Create",
        ["Db_DeleteConfirm_Title"] = "Delete Game",
        ["Db_DeleteConfirm_Warning"] = "⚠️ Confirmation Required",
        ["Db_DeleteConfirm_Question"] = "Are you sure you want to delete this game from {0}?",
        ["Db_DeleteConfirm_Irreversible"] = "This action cannot be undone. Do you want to continue?",
        ["Db_You"] = "You",
        ["Db_CleanForeign_Title"] = "🧹 Clean Foreign Games from {0}",
        ["Db_CleanForeign_Scanning"] = "Scanning for foreign games...",
        ["Db_CleanForeign_AllClean"] = "All Clean!",
        ["Db_CleanForeign_AllCleanDesc"] = "All games in \"{0}\" match your profile handles. No foreign or scouted games found.",
        ["Db_CleanForeign_Detected"] = "⚠️ Non-Profile Games Detected",
        ["Db_CleanForeign_Found"] = "Found {0} {1} that do not belong to your online accounts:",
        ["Db_CleanForeign_ActiveHandles"] = "Active Profile Handles:",
        ["Db_CleanForeign_Irreversible"] = "Purging will remove these foreign/scouted games and keep only your personal online matches.",
        ["Db_CleanForeign_PurgeBtn"] = "Purge {0} Foreign Games",

        // Opponent Dossier
        ["Dossier_LocalDb"] = "💾 Local DB",
        ["Dossier_FideWeb"] = "🌐 FIDE & Web",
        ["Dossier_SearchPlaceholder_Local"] = "Search opponent name (e.g. Kasparov, Carlsen)...",
        ["Dossier_SearchPlaceholder_Fide"] = "Enter FIDE ID (e.g. 1503014) or Name...",
        ["Dossier_ScoutOpponent"] = "Scout Opponent",
        ["Dossier_ScoutFide"] = "Scout FIDE & Web",
        ["Dossier_FetchOnline"] = "Fetch Online Games",
        ["Dossier_Generating"] = "Generating Scouting Dossier...",
        ["Dossier_GeneratingDesc"] = "Analyzing game records, parsing opening lines, and computing tactical tendencies.",
        ["Dossier_NoOpponent"] = "No Opponent Selected",
        ["Dossier_NoOpponentDesc"] = "Type a player's name above to generate a comprehensive pre-game scouting report, or fetch their latest games from Lichess / Chess.com.",
        ["Dossier_ExportGames"] = "💾 Export Games ({0})",
        ["Dossier_ImportChessResults"] = "Import from Chess-Results",
        ["Dossier_Importing"] = "Importing...",
        ["Dossier_TotalGames"] = "Total Games",
        ["Dossier_Score"] = "{0}% score ({1} W / {2} D / {3} L)",
        ["Dossier_PlayingWhite"] = "When Playing White",
        ["Dossier_PlayingBlack"] = "When Playing Black",
        ["Dossier_Priority_High"] = "High Priority",
        ["Dossier_Priority_Moderate"] = "Moderate Priority",
        ["Dossier_Recommendation"] = "🎯 Recommendation: {0}",
        ["Dossier_Nav_WhiteRep"] = "⚪ White Repertoire ({0})",
        ["Dossier_Nav_BlackRep"] = "⚫ Black Repertoire ({0})",
        ["Dossier_Nav_Style"] = "📊 Playing Style & Phases",
        ["Dossier_Nav_Games"] = "🗄️ Games Archive ({0})",
        ["Dossier_Nav_Tournaments"] = "🏆 OTB Tournaments ({0})",
        ["Dossier_PeakElo"] = "Peak Elo: {0}",
        ["Dossier_AvgElo"] = "Avg Elo: {0}",
        ["Dossier_Style"] = "Style: {0}",
        ["Dossier_NoWhiteGames"] = "No games as White found",
        ["Dossier_NoBlackGames"] = "No games as Black found",

        // Endgame Trainer
        ["Endgame_WhiteToMove"] = "White to move",
        ["Endgame_BlackToMove"] = "Black to move",
        ["Endgame_TargetWin"] = "Target: Win as {0}",
        ["Endgame_TargetDraw"] = "Target: Draw as {0}",
        ["Endgame_TargetWinShort"] = "Win as {0}",
        ["Endgame_TargetDrawShort"] = "Draw as {0}",
        ["Endgame_OpponentThinking"] = "Opponent thinking...",
        ["Endgame_ClassicalEndgames"] = "🏆 Classical Endgames",
        ["Endgame_Mastered"] = "Mastered: {0} / {1} ({2}%)",
        ["Endgame_Cat_All"] = "All",
        ["Endgame_Cat_Pawns"] = "Pawns",
        ["Endgame_Cat_Rooks"] = "Rooks",
        ["Endgame_Cat_Queens"] = "Queens",
        ["Endgame_Cat_MinorPieces"] = "Minor Pieces",
        ["Endgame_Cat_Practical"] = "Practical",
        ["Endgame_KeySquares"] = "Key Squares:",
        ["Endgame_Reset"] = "↻ Reset",
        ["Endgame_Hint"] = "💡 Hint",
        ["Endgame_HideHint"] = "Hide Hint",
        ["Endgame_Tablebase"] = "⚡ Tablebase",
        ["Endgame_HideMoves"] = "Hide Moves",
        ["Endgame_Next"] = "Next ▶",
        ["Endgame_TbCandidates"] = "⚡ Tablebase Candidate Moves",

        // Puzzle Trainer
        ["Puzzle_Loading"] = "Loading {0}...",
        ["Puzzle_EstRating"] = "🎯 Est. Rating: {0}",
        ["Puzzle_NumOf"] = "#{0} of {1}",
        ["Puzzle_WhiteToMove"] = "White to move",
        ["Puzzle_BlackToMove"] = "Black to move",
        ["Puzzle_NoBlundersYet"] = "No personal blunder drills yet",
        ["Puzzle_NoBlundersDesc"] = "Caissalytics generates personal drills from games where you made a mistake or blunder. Sync your online games or analyze games with Stockfish, then click Scan Games for Blunders below.",
        ["Puzzle_ReviewQueueClear"] = "Review queue is clear!",
        ["Puzzle_ReviewQueueDesc"] = "Puzzles you fail during practice will automatically appear here for spaced repetition.",
        ["Puzzle_NoPuzzles"] = "No puzzles found",
        ["Puzzle_NoPuzzlesDesc"] = "Try selecting another category above or scan your database.",
        ["Puzzle_Rating"] = "Puzzle Rating",
        ["Puzzle_CurrentStreak"] = "Current Streak",
        ["Puzzle_Solved"] = "Solved",
        ["Puzzle_Accuracy"] = "Accuracy",
        ["Puzzle_Mode_All"] = "All",
        ["Puzzle_Mode_Blunders"] = "My Blunders",
        ["Puzzle_Mode_Master"] = "Master Tactics",
        ["Puzzle_Mode_Review"] = "Review",
        ["Puzzle_YourRealGame"] = "Your Real Game",
        ["Puzzle_YourTurn"] = "Your turn! Find the best move for {0}",
        ["Puzzle_White"] = "White",
        ["Puzzle_Black"] = "Black",
        ["Puzzle_GoodMoveContinuation"] = "Good move! Now find the continuation...",
        ["Puzzle_SolvedBonus"] = "🎉 Solved! +15 Rating",
        ["Puzzle_PlayedBlunder"] = "⚠️ You played the real-game blunder!",
        ["Puzzle_WrongMove"] = "❌ Not quite the best move.",
        ["Puzzle_WrongMoveDesc"] = "Try again or reveal the tactical solution below.",
        ["Puzzle_SolutionRevealed"] = "💡 Solution Revealed",
        ["Puzzle_WinningMoves"] = "Winning moves: {0}",
        ["Puzzle_NextPuzzle"] = "Next Puzzle",
        ["Puzzle_Solution"] = "Solution",
        ["Puzzle_Reset"] = "Reset",
        ["Puzzle_Skip"] = "Skip",
        ["Puzzle_Analyze"] = "Analyze",
        ["Puzzle_ExtractTitle"] = "Extract My Blunders",
        ["Puzzle_ExtractDesc"] = "Automatically scan your synced online games (or active database) for mistakes and generate personal blunder drills.",
        ["Puzzle_ScanBtn"] = "🔍 Scan Games for Blunders",
        ["Puzzle_Scanning"] = "⏳ Scanning games...",

        // Opening Tree & Repertoire
        ["Rep_Source"] = "Source:",
        ["Rep_LocalDbs"] = "Local Databases",
        ["Rep_OnlineExplorer"] = "Online Explorer (Free)",
        ["Rep_AllMoves"] = "All Moves",
        ["Rep_WhiteRep"] = "White Repertoire",
        ["Rep_BlackRep"] = "Black Repertoire",
        ["Rep_CandidateMoves"] = "Candidate Moves",
        ["Rep_GamesCount"] = "{0} games",
        ["Rep_Move"] = "Move",
        ["Rep_Games"] = "Games",
        ["Rep_Freq"] = "Freq",
        ["Rep_Score"] = "Score",
        ["Rep_Distribution"] = "Distribution",
        ["Rep_AvgElo"] = "Avg Elo",
        ["Rep_Years"] = "Years",
        ["Rep_TopGames"] = "Top Games Reaching Position",
        ["Rep_MoveStatus"] = "Move Status",
        ["Rep_MainLine"] = "Main Line (Primary recommendation)",
        ["Rep_Alternative"] = "Alternative (Solid secondary option)",
        ["Rep_Surprise"] = "Surprise Weapon (Tactical / blitz line)",
        ["Rep_PersonalNotes"] = "Personal Notes & Preparation",
        ["Rep_NotesPlaceholder"] = "Add tactical ideas, key plans, or warnings for this move...",
        ["Rep_RemoveFromRep"] = "Remove from Repertoire",
        ["Rep_SaveMove"] = "Save Move",
        ["Rep_LichessTokenRequired"] = "Lichess Token Required",
        ["Rep_LichessTokenDesc"] = "Lichess now requires a free Personal Access Token to query online opening statistics. You can generate one for free on Lichess in seconds.",
        ["Rep_ConfigureInSettings"] = "⚙️ Configure in Settings",
        ["Rep_GetFreeToken"] = "🔗 Get Free Token ↗",
        ["Rep_OfflineHint"] = "💡 Tip: Local databases (TWIC, Extraliga) never require a token and work 100% offline!",
        ["Rep_NoRecordedMoves"] = "No recorded moves from this position.",
        ["Rep_NoRecordedGames"] = "No recorded games for this position.",
        ["Rep_LoadingStats"] = "Loading tree statistics...",
        ["Rep_DisplayedCount"] = "{0} displayed",
        ["Rep_ColRep"] = "Rep",
        ["Rep_InRepertoire"] = "In Repertoire",
        ["Rep_ClickToEdit"] = "Click to edit note",
        ["Rep_AddToRepertoire"] = "Add to personal repertoire",
        ["Rep_ModalTitle"] = "Repertoire",
        ["Rep_Main_Badge"] = "Main Line",
        ["Rep_Alt_Badge"] = "Alternative",
        ["Rep_Surprise_Badge"] = "Surprise",
        ["Rep_MyLines"] = "My Opening Lines",
        ["Rep_SaveLine"] = "Save as Opening Line",
        ["Rep_LineName"] = "Opening Name",
        ["Rep_LineNamePlaceholder"] = "e.g. Spanish Opening, Main Variation",
        ["Rep_LineDesc"] = "Description (optional)",
        ["Rep_LineDescPlaceholder"] = "Key ideas, move order tricks, typical plans...",
        ["Rep_LineColor"] = "Color",
        ["Rep_LineSave"] = "Save Line",
        ["Rep_LineDelete"] = "Delete Line",
        ["Rep_LineDeleteConfirm"] = "Delete this opening line?",
        ["Rep_LineMoves"] = "{0} moves",
        ["Rep_NoLines"] = "No saved opening lines yet.",
        ["Rep_NoLinesHint"] = "Navigate moves on the board and click 'Save as Opening Line' to build your repertoire.",
        ["Rep_TrainLine"] = "Train",
        ["Rep_TrainTitle"] = "Training: {0}",
        ["Rep_TrainYourMove"] = "Your move!",
        ["Rep_TrainCorrect"] = "Correct!",
        ["Rep_TrainWrong"] = "Wrong! The correct move was {0}.",
        ["Rep_TrainComplete"] = "Training Complete!",
        ["Rep_TrainScore"] = "{0} / {1} correct",
        ["Rep_TrainAgain"] = "Train Again",
        ["Rep_TrainExit"] = "Exit Training",
        ["Rep_TrainProgress"] = "Position {0} of {1}",
        ["Rep_TrainSkip"] = "Show Answer",
        ["Rep_ReplayLine"] = "Replay",
        ["General_Unknown"] = "Unknown",

        // Board & Chessground Controls
        ["Board_FirstMove"] = "First Move",
        ["Board_PrevMove"] = "Previous Move",
        ["Board_NextMove"] = "Next Move",
        ["Board_LastMove"] = "Last Move",
        ["Board_FlipBoard"] = "Flip Board",
        ["Board_You"] = "You",
        ["Board_Promotion_Queen"] = "Queen",
        ["Board_Promotion_Rook"] = "Rook",
        ["Board_Promotion_Bishop"] = "Bishop",
        ["Board_Promotion_Knight"] = "Knight",

        // Board Editor
        ["BoardEditor_Title"] = "Board Editor",
        ["BoardEditor_EditPosition"] = "Edit Position",
        ["BoardEditor_StartingPos"] = "Starting Position",
        ["BoardEditor_ClearBoard"] = "Clear Board",
        ["BoardEditor_InvertColors"] = "Invert Colors",
        ["BoardEditor_PiecePalette"] = "Piece Palette",
        ["BoardEditor_WhitePieces"] = "White Pieces",
        ["BoardEditor_BlackPieces"] = "Black Pieces",
        ["BoardEditor_EraseSquare"] = "Erase Tool",
        ["BoardEditor_RightClickHint"] = "Tip: Left-click places piece, right-click erases square",
        ["BoardEditor_PositionRules"] = "Position Rules",
        ["BoardEditor_SideToMove"] = "Side to move",
        ["BoardEditor_CastlingRights"] = "Castling Rights",
        ["BoardEditor_LoadFen"] = "Load FEN",
        ["BoardEditor_CopyFen"] = "Copy FEN",
        ["BoardEditor_PositionValid"] = "Position is valid",
        ["BoardEditor_NoWhiteKing"] = "Position must have exactly one White king (none found).",
        ["BoardEditor_MultipleWhiteKings"] = "Position must have exactly one White king (multiple found).",
        ["BoardEditor_NoBlackKing"] = "Position must have exactly one Black king (none found).",
        ["BoardEditor_MultipleBlackKings"] = "Position must have exactly one Black king (multiple found).",
        ["BoardEditor_PawnsOnBackRank"] = "Pawns cannot be placed on the 1st or 8th rank.",
        ["BoardEditor_KingsAdjacent"] = "Kings cannot be placed on adjacent squares.",

        // Printable Homework & Diagrams
        ["Homework_Title"] = "Homework & Diagram Sheets",
        ["Homework_Subtitle"] = "Create, customize, and print high-quality chess worksheets for students and coaching sessions.",
        ["Homework_NewSheet"] = "New Sheet",
        ["Homework_DiagramsCount"] = "diagrams",
        ["Homework_PerRow"] = "per row",
        ["Homework_NoSheets"] = "No homework sheets created yet",
        ["Homework_NoSheetsDesc"] = "Create a blank worksheet or choose from coaching templates to generate printable materials.",
        ["Homework_CreateFirst"] = "Create First Sheet",
        ["Homework_StudentHeader"] = "Student Header",
        ["Homework_AnswerKeyBadge"] = "Answer Key",
        ["Homework_Edit"] = "Edit Sheet",
        ["Homework_PrintPreview"] = "Print Preview",
        ["Homework_Print"] = "Print / Export PDF",
        ["Homework_Duplicate"] = "Duplicate",
        ["Homework_Delete"] = "Delete",
        ["Homework_AllSheets"] = "All Sheets",
        ["Homework_SheetTitle"] = "Sheet Title",
        ["Homework_Instructions"] = "Instructions / Subtitle",
        ["Homework_CoachName"] = "Coach Name",
        ["Homework_ClubName"] = "Chess Club / School",
        ["Homework_DiagramsPerRow"] = "Diagrams Per Row",
        ["Homework_Columns"] = "Columns",
        ["Homework_ShowCoordinates"] = "Show Board Coordinates",
        ["Homework_ShowSolutionLines"] = "Show Handwriting Solution Lines",
        ["Homework_IncludeAnswerKey"] = "Include Teacher Answer Key (Page 2)",
        ["Homework_DiagramsList"] = "Diagrams & Exercises",
        ["Homework_DiagramsListDesc"] = "Add, reorder, or edit positions, prompts, and solutions.",
        ["Homework_ImportFromAnalysis"] = "Import from Active Analysis",
        ["Homework_AddDiagram"] = "Add Blank Diagram",
        ["Homework_Orientation"] = "Orientation",
        ["Homework_ExercisePrompt"] = "Exercise Prompt",
        ["Homework_Solution"] = "Solution",
        ["Homework_TeacherKey"] = "Teacher Key",
        ["Homework_BackToEditor"] = "Back to Editor",
        ["Homework_StudentName"] = "Name",
        ["Homework_Date"] = "Date",
        ["Homework_Score"] = "Score",
        ["Homework_AnswerKeyTitle"] = "Teacher Answer Key",
        ["Homework_AnswerKeyDesc"] = "Official solutions and key for evaluation and grading.",
        ["Homework_DeleteConfirmTitle"] = "Delete Homework Sheet",
        ["Homework_DeleteConfirmDesc"] = "Are you sure you want to delete '{0}'? This action cannot be undone."
    };

    private static Dictionary<string, string> GetSlovakStrings() => new(StringComparer.OrdinalIgnoreCase)
    {
        // App Navigation & Header
        ["Nav_Brand"] = "Caissalytics",
        ["Nav_Insights"] = "Prehľady",
        ["Nav_ControlCenter"] = "Riadiace centrum",
        ["Nav_NewBoard"] = "+ Nová šachovnica",
        ["Nav_Database"] = "Databáza",
        ["Nav_Update"] = "Aktualizácia v{0}",

        // Dashboard Hub
        ["Dash_Welcome"] = "Vitajte v Caissalytics",
        ["Dash_Subtitle"] = "Moderná open-source platforma na analýzu partií a správu šachových databáz.",
        ["Dash_Card_Analysis"] = "Nová analýza partie",
        ["Dash_Card_Analysis_Desc"] = "Otvorte interaktívnu šachovnicu na skúmanie ťahov, vetvenie variantov, anotácie a analýzu so Stockfishom.",
        ["Dash_Card_Databases"] = "Prehliadať databázy",
        ["Dash_Card_Databases_Desc"] = "Spravujte zbierky, vyhľadávajte partie podľa hráčov, ECO kódu či Ela a importujte PGN súbory.",
        ["Dash_Card_OpenPgn"] = "Otvoriť PGN súbor",
        ["Dash_Card_OpenPgn_Desc"] = "Nahrajte partiu alebo štúdiu z počítača s kompletnou notáciou, variantmi a komentármi.",
        ["Dash_Card_OnlineGames"] = "Moje online partie",
        ["Dash_Card_OnlineGames_Desc"] = "Synchronizujte a analyzujte svoje partie z Lichessu a Chess.com priamo v lokálnej databáze.",
        ["Dash_Card_Puzzles"] = "Riešiť vlastné hrubky",
        ["Dash_Card_Puzzles_Desc"] = "Trénujte taktické motívy a nápravu chýb z vašich vlastných partií aj majstrovské kombinácie.",
        ["Dash_Card_Repertoire"] = "Strom otvorení & Repertoár",
        ["Dash_Card_Repertoire_Desc"] = "Skúmajte frekvenciu ťahov, úspešnosť a majstrovské partie z databáz a budujte si osobný repertoár.",
        ["Dash_Card_Dossier"] = "Príprava na súpera",
        ["Dash_Card_Dossier_Desc"] = "Vytvorte si podrobný profil turnajového súpera, analyzujte jeho repertoár a odhaľte slabiny.",
        ["Dash_Card_Endgames"] = "Tréning koncoviek",
        ["Dash_Card_Endgames_Desc"] = "Zvládnite teoretické koncovky v hre proti Stockfishu s overením cez Syzygy tabuľky a cieľmi.",
        ["Dash_Card_Homework"] = "Pracovné listy & diagramy",
        ["Dash_Card_Homework_Desc"] = "Vytvárajte a tlačte vlastné šachové úlohy s diagramami, hlavičkou pre študenta a kľúčom riešení.",
        ["Dash_Card_Insights_Desc"] = "Sledujte svoju bilanciu výhier/prehier, štatistiky za bieleho a čierneho, vývoj ratingu a repertoár.",
        ["Dash_Card_Settings"] = "Riadiace centrum & Nastavenia",
        ["Dash_Card_Settings_Desc"] = "Nastavte šachové motory, cesty k Syzygy tabuľkám, profil hráča, synchronizáciu a vzhľad šachovnice.",

        // Settings Navigation Categories
        ["Settings_Title"] = "Nastavenia & Riadiace centrum",
        ["Settings_Subtitle"] = "Nastavte profil hráča, šachové motory, databázy a vizuálne predvoľby.",
        ["Settings_Nav_Profile"] = "Profil & Účty",
        ["Settings_Nav_Profile_Desc"] = "Meno, FIDE ID, online účty",
        ["Settings_Nav_Appearance"] = "Rozhranie, vzhľad & zvuk",
        ["Settings_Nav_Appearance_Desc"] = "Jazyk, témy, zvukové efekty, hlasitosť",
        ["Settings_Nav_Engines"] = "Šachové motory",
        ["Settings_Nav_Engines_Desc"] = "Nainštalované motory, aktívny UCI, inštalácia jedným klikom",
        ["Settings_Nav_Tablebases"] = "Syzygy tabuľky",
        ["Settings_Nav_Tablebases_Desc"] = "Vyhľadávanie v koncovkách, lokálna cesta, online API",
        ["Settings_Nav_Sync"] = "Synchronizácia online partií",
        ["Settings_Nav_Sync_Desc"] = "Veľkosť dávky, stav databázy, automatická synchronizácia",
        ["Settings_Nav_Updates"] = "Aplikácia & Aktualizácie",
        ["Settings_Nav_Updates_Desc"] = "Verzia, vydania, automatická aktualizácia",

        // Language Selection Section
        ["Settings_Lang_Title"] = "Jazyk aplikácie",
        ["Settings_Lang_Desc"] = "Vyberte si preferovaný jazyk. Zmeny sa okamžite prejavia na všetkých obrazovkách.",
        ["Settings_Lang_Active"] = "Aktívny",

        // Appearance / Board Themes
        ["Settings_BoardThemes_Title"] = "Grafické témy šachovnice",
        ["Settings_BoardThemes_Desc"] = "Vyberte si štýl šachovnice. Ostré zobrazenie vo všetkých rozlíšeniach.",
        ["Settings_SoundEngine_Title"] = "Zvukový systém",
        ["Settings_SoundEngine_Desc"] = "Autentické zvukové efekty s natívnou odozvou systému.",
        ["Settings_Sound_Master"] = "Zapnúť zvukové efekty",
        ["Settings_Sound_Volume"] = "Hlasitosť zvuku",
        ["Settings_Sound_Move"] = "Ťah figúrou",
        ["Settings_Sound_Capture"] = "Branie figúry",
        ["Settings_Sound_Check"] = "Šach",
        ["Settings_Sound_Victory"] = "Víťazstvo & Mat",
        ["Settings_Sound_LowTime"] = "Varovanie pred časom",
        ["Settings_Sound_Test"] = "Vyskúšať",

        // Common Actions
        ["Common_Save"] = "Uložiť nastavenia",
        ["Common_Cancel"] = "Zrušiť",
        ["Common_Delete"] = "Zmazať",
        ["Common_Remove"] = "Odstrániť",
        ["Common_Active"] = "Aktívne",
        ["Common_Installed"] = "Nainštalované",
        ["Common_Update"] = "Aktualizovať",
        ["Common_Close"] = "Zavrieť",
        ["Common_MoveUp"] = "Posunúť nahor",
        ["Common_MoveDown"] = "Posunúť nadol",
        ["Common_PreferencesSaved"] = "✓ Nastavenia boli uložené!",

        // Engine Removal Dialog
        ["Engine_RemoveConfirm_Title"] = "Odstránenie šachového motora",
        ["Engine_RemoveConfirm_Warning"] = "Potvrďte odstránenie motora",
        ["Engine_RemoveConfirm_Question"] = "Naozaj chcete odstrániť motor {0}?",
        ["Engine_RemoveConfirm_ActiveWarning"] = "Tento motor je momentálne nastavený ako váš aktívny motor. Po odstránení Caissalytics automaticky prepne na iný dostupný motor.",
        ["Engine_RemoveConfirm_Btn"] = "Odstrániť motor",

        // Workspace Tabs
        ["Tab_Dashboard"] = "Prehľad",
        ["Tab_Analysis"] = "Analýza partie",
        ["Tab_Database"] = "Databáza",
        ["Tab_Settings"] = "Riadiace centrum",
        ["Tab_Analytics"] = "Osobné prehľady",
        ["Tab_Puzzles"] = "Taktické rébusy",
        ["Tab_Repertoire"] = "Strom otvorení",
        ["Tab_OpponentDossier"] = "Príprava na súpera",
        ["Tab_Endgames"] = "Koncovky",
        ["Tab_Homework"] = "Pracovné listy & diagramy",

        // Analysis Workbench
        ["Analysis_Tab_Moves"] = "Ťahy & Anotácie",
        ["Analysis_Tab_Report"] = "Analýza partie",
        ["Analysis_Tab_Reference"] = "Prehľad otvorení",
        ["Analysis_Tab_Tablebase"] = "🏆 Syzygy tabuľky",
        ["Analysis_Tab_Tools"] = "PGN & FEN",
        ["Analysis_SaveToDb"] = "💾 Uložiť do DB",
        ["Analysis_SaveGameToDb"] = "💾 Uložiť partiu do databázy",
        ["Analysis_GameHeaders"] = "Hlavičky partie",
        ["Analysis_White"] = "Biely:",
        ["Analysis_Black"] = "Čierny:",
        ["Analysis_Event"] = "Podujatie:",
        ["Analysis_Result"] = "Výsledok:",
        ["Analysis_Fen"] = "FEN",
        ["Analysis_PgnExport"] = "Export PGN",
        ["Analysis_CopyFen"] = "📋 Kopírovať FEN",
        ["Analysis_CopyPgn"] = "📋 Kopírovať PGN",
        ["Analysis_Copied"] = "✅ Skopírované!",
        ["Analysis_CasualAnalysis"] = "Bežná analýza",
        ["Analysis_AddComment"] = "Pridať komentár k ťahu...",
        ["Analysis_SaveComment"] = "Uložiť",
        ["Analysis_PromoteVariation"] = "⬆ Hlavný",
        ["Analysis_DeleteVariation"] = "Zmazať variant",
        ["Analysis_ClearGlyphs"] = "Zmazať značky",

        // Engine Analysis Panel
        ["Engine_Stop"] = "⏹ Stop",
        ["Engine_Start"] = "▶ Štart",
        ["Engine_Depth"] = "hĺbka",
        ["Engine_Nps"] = "nps",
        ["Engine_Idle"] = "Motor nečinný",
        ["Engine_Line_1"] = "1 variant",
        ["Engine_Line_2"] = "2 varianty",
        ["Engine_Line_3"] = "3 varianty",
        ["Engine_Line_4"] = "4 varianty",
        ["Engine_Line_5"] = "5 variantov",
        ["Engine_AddVariation"] = "+ Variant",
        ["Engine_NotInstalled"] = "Šachový motor zatiaľ nie je nainštalovaný",
        ["Engine_NotInstalledDesc"] = "Umožnite hĺbkovú analýzu jedným klikom, živý stĺpec hodnotenia a šípky najlepších ťahov.",
        ["Engine_Installing"] = "Sťahovanie a rozbaľovanie oficiálneho súboru ({0}%)...",
        ["Engine_Install1Click"] = "⚡ Inštalovať jedným klikom",
        ["Engine_ClickStart"] = "Kliknite na ▶ Štart pre analýzu kandidátskych ťahov so Stockfishom",
        ["Engine_Evaluating"] = "Prebieha analýza pozície...",
        ["Engine_UpdatingStockfish"] = "Aktualizuje sa ({0}%)...",
        ["Engine_UpdateStockfishBtn"] = "⚡ Aktualizovať na {0}",

        // Game Report Panel
        ["Report_FullGameAnalysis"] = "Kompletná analýza partie",
        ["Report_FullGameDesc"] = "Nechajte Stockfish prejsť každý ťah partie, vyhodnotiť presnosť hráčov, odhaliť hrubé chyby a zobraziť krivku hodnotenia.",
        ["Report_QuickScan"] = "Rýchly sken (80ms)",
        ["Report_Standard"] = "Štandard (150ms)",
        ["Report_Deep"] = "Hĺbková analýza (400ms)",
        ["Report_AnalyzeEntireGame"] = "📊 Analyzovať celú partiu",
        ["Report_AnalyzingWithStockfish"] = "Analyzujem partiu so Stockfishom...",
        ["Report_MoveProgress"] = "Ťah {0} z {1} ({2})",
        ["Report_Classification"] = "Klasifikácia kvality ťahov",
        ["Report_Best"] = "Najlepší",
        ["Report_Excellent"] = "Výborný",
        ["Report_Good"] = "Dobrý",
        ["Report_Inaccuracy"] = "Nepresnosť (?!)",
        ["Report_Mistake"] = "Chyba (?)",
        ["Report_Blunder"] = "Hrubá chyba (??)",
        ["Report_AnnotateTree"] = "✨ Anotovať strom ťahov",
        ["Report_TreeAnnotated"] = "✓ Strom anotovaný",
        ["Report_NextMistake"] = "🔍 Ďalšia chyba ▶",

        // Tablebase Panel
        ["Tablebase_Title"] = "Syzygy tabuľky",
        ["Tablebase_Subheading"] = "{0} figúr na šachovnici • 7-figúrové Syzygy",
        ["Tablebase_Refresh"] = "↻ Obnoviť",
        ["Tablebase_Probing"] = "Vyhľadávam v Syzygy tabuľkách...",
        ["Tablebase_ExactMate"] = "Presný mat: {0} polťahov",
        ["Tablebase_DistanceToZero"] = "Vzdialenosť do nuly (DTZ): {0} polťahov",
        ["Tablebase_Checkmate"] = "Mat",
        ["Tablebase_Stalemate"] = "Pat",
        ["Tablebase_Move"] = "Ťah",
        ["Tablebase_Result"] = "Výsledok",
        ["Tablebase_Dtz"] = "DTZ",
        ["Tablebase_Dtm"] = "DTM",
        ["Tablebase_Action"] = "Akcia",

        // Save Game Modal
        ["SaveGame_Title"] = "💾 Uložiť partiu do databázy",
        ["SaveGame_TargetDb"] = "Cieľová databáza",
        ["SaveGame_SaveMode"] = "Režim uloženia",
        ["SaveGame_Overwrite"] = "Prepísať existujúci záznam (#{0})",
        ["SaveGame_OverwriteDesc"] = "Aktualizuje existujúcu partiu a jej pozície v {0}",
        ["SaveGame_SaveAsNew"] = "Uložiť ako novú partiu",
        ["SaveGame_SaveAsNewDesc"] = "Pôvodnú partiu #{0} ponechá nezmenenú a vytvorí nový záznam",
        ["SaveGame_WhitePlayer"] = "Biely hráč",
        ["SaveGame_WhiteElo"] = "Elo bieleho",
        ["SaveGame_BlackPlayer"] = "Čierny hráč",
        ["SaveGame_BlackElo"] = "Elo čierneho",
        ["SaveGame_Event"] = "Podujatie / Turnaj",
        ["SaveGame_Site"] = "Miesto konania",
        ["SaveGame_Round"] = "Kolo",
        ["SaveGame_Date"] = "Dátum",
        ["SaveGame_Result"] = "Výsledok",
        ["SaveGame_Eco"] = "ECO kód",
        ["SaveGame_Saving"] = "Ukladám...",
        ["SaveGame_BtnSave"] = "Uložiť partiu",

        // Database Browser
        ["Db_Databases"] = "Databázy",
        ["Db_NewDb"] = "+ Nová DB",
        ["Db_Reference"] = "⭐ Referenčná",
        ["Db_ReferenceDb"] = "⭐ Referenčná DB",
        ["Db_SetRef"] = "⭐ Nastaviť ref.",
        ["Db_SetAsReference"] = "⭐ Nastaviť ako referenčnú",
        ["Db_Sync"] = "🔄 Sync",
        ["Db_SyncGames"] = "🔄 Synchronizovať partie",
        ["Db_Delete"] = "🗑️ Zmazať",
        ["Db_MasterLib"] = "📚 Majstrovská knižnica",
        ["Db_AddNewGame"] = "+ Pridať novú partiu",
        ["Db_ImportPgn"] = "📥 Importovať PGN",
        ["Db_CleanForeign"] = "🧹 Vyčistiť cudzie partie",
        ["Db_GamesCount"] = "{0} partií",
        ["Db_Filter_Player"] = "Hráč",
        ["Db_Filter_PlayerPlaceholder"] = "napr. Kasparov, Fischer",
        ["Db_Filter_Eco"] = "ECO",
        ["Db_Filter_MinElo"] = "Min. Elo",
        ["Db_Filter_Result"] = "Výsledok",
        ["Db_Filter_All"] = "Všetky",
        ["Db_Filter_WhiteWin"] = "1-0 (Výhra bieleho)",
        ["Db_Filter_BlackWin"] = "0-1 (Výhra čierneho)",
        ["Db_Filter_Draw"] = "½-½ (Remíza)",
        ["Db_Filter_Event"] = "Podujatie",
        ["Db_Filter_EventPlaceholder"] = "Turnaj / Podujatie",
        ["Db_Filter_Search"] = "Hľadať",
        ["Db_Filter_Reset"] = "Reset",
        ["Db_Col_Site"] = "Server / Miesto",
        ["Db_Col_White"] = "Biely",
        ["Db_Col_Black"] = "Čierny",
        ["Db_Col_Elo"] = "Elo",
        ["Db_Col_Result"] = "Výsledok",
        ["Db_Col_Eco"] = "ECO",
        ["Db_Col_Date"] = "Dátum",
        ["Db_Col_Event"] = "Podujatie",
        ["Db_Col_Moves"] = "Ťahy",
        ["Db_OpenGame"] = "Otvoriť",
        ["Db_DeleteGame"] = "Zmazať partiu",
        ["Db_ShowingGames"] = "Zobrazené {0} - {1} z {2} partií",
        ["Db_PrevPage"] = "◀ Späť",
        ["Db_NextPage"] = "Ďalej ▶",
        ["Db_PageOf"] = "Strana {0} z {1}",
        ["Db_LoadingGames"] = "Načítavam partie...",
        ["Db_NoGamesMatch"] = "Žiadne partie nezodpovedajú vyhľadávaniu.",
        ["Db_NoGamesMatchDesc"] = "Skúste upraviť filtre alebo importovať partie z PGN súborov.",
        ["Db_CreateDb_Title"] = "Vytvoriť novú databázu",
        ["Db_CreateDb_Name"] = "Názov databázy",
        ["Db_CreateDb_Placeholder"] = "napr. Majstri, Sicílska, Blitz",
        ["Db_CreateDb_Btn"] = "Vytvoriť",
        ["Db_DeleteConfirm_Title"] = "Zmazať partiu",
        ["Db_DeleteConfirm_Warning"] = "⚠️ Vyžaduje sa potvrdenie",
        ["Db_DeleteConfirm_Question"] = "Naozaj chcete zmazať túto partiu z {0}?",
        ["Db_DeleteConfirm_Irreversible"] = "Túto akciu nemožno vrátiť späť. Chcete pokračovať?",
        ["Db_You"] = "Vy",
        ["Db_CleanForeign_Title"] = "🧹 Vyčistiť cudzie partie z {0}",
        ["Db_CleanForeign_Scanning"] = "Hľadám cudzie partie...",
        ["Db_CleanForeign_AllClean"] = "Všetko je čisté!",
        ["Db_CleanForeign_AllCleanDesc"] = "Všetky partie v \"{0}\" zodpovedajú vašim online účtom. Nenašli sa žiadne cudzie partie.",
        ["Db_CleanForeign_Detected"] = "⚠️ Zistené partie iných hráčov",
        ["Db_CleanForeign_Found"] = "Našlo sa {0} {1}, ktoré nepatria k vašim online účtom:",
        ["Db_CleanForeign_ActiveHandles"] = "Aktívne účty profilu:",
        ["Db_CleanForeign_Irreversible"] = "Vyčistenie odstráni tieto stiahnuté partie a ponechá len vaše vlastné online súboje.",
        ["Db_CleanForeign_PurgeBtn"] = "Odstrániť {0} cudzích partií",

        // Opponent Dossier
        ["Dossier_LocalDb"] = "💾 Lokálna DB",
        ["Dossier_FideWeb"] = "🌐 FIDE & Web",
        ["Dossier_SearchPlaceholder_Local"] = "Hľadať meno súpera (napr. Kasparov, Carlsen)...",
        ["Dossier_SearchPlaceholder_Fide"] = "Zadajte FIDE ID (napr. 1503014) alebo meno...",
        ["Dossier_ScoutOpponent"] = "Preskúmať súpera",
        ["Dossier_ScoutFide"] = "Hľadať na FIDE & Webe",
        ["Dossier_FetchOnline"] = "Stiahnuť online partie",
        ["Dossier_Generating"] = "Vytváram profil súpera...",
        ["Dossier_GeneratingDesc"] = "Analyzujem záznamy partií, skúmam otvorenie a taktické tendencie.",
        ["Dossier_NoOpponent"] = "Nie je vybraný žiadny súper",
        ["Dossier_NoOpponentDesc"] = "Zadajte meno hráča vyššie pre vytvorenie predzápasového profilu, alebo stiahnite jeho partie z Lichessu či Chess.com.",
        ["Dossier_ExportGames"] = "💾 Exportovať partie ({0})",
        ["Dossier_ImportChessResults"] = "Importovať z Chess-Results",
        ["Dossier_Importing"] = "Importujem...",
        ["Dossier_TotalGames"] = "Celkovo partií",
        ["Dossier_Score"] = "{0}% úspešnosť ({1} V / {2} R / {3} P)",
        ["Dossier_PlayingWhite"] = "Pri hre za bieleho",
        ["Dossier_PlayingBlack"] = "Pri hre za čierneho",
        ["Dossier_Priority_High"] = "Vysoká priorita",
        ["Dossier_Priority_Moderate"] = "Stredná priorita",
        ["Dossier_Recommendation"] = "🎯 Odporúčanie: {0}",
        ["Dossier_Nav_WhiteRep"] = "⚪ Repertoár za bieleho ({0})",
        ["Dossier_Nav_BlackRep"] = "⚫ Repertoár za čierneho ({0})",
        ["Dossier_Nav_Style"] = "📊 Štýl hry & Fázy",
        ["Dossier_Nav_Games"] = "🗄️ Archív partií ({0})",
        ["Dossier_Nav_Tournaments"] = "🏆 OTB Turnaje ({0})",
        ["Dossier_PeakElo"] = "Max Elo: {0}",
        ["Dossier_AvgElo"] = "Priem. Elo: {0}",
        ["Dossier_Style"] = "Štýl: {0}",
        ["Dossier_NoWhiteGames"] = "Nenašli sa žiadne partie za bieleho",
        ["Dossier_NoBlackGames"] = "Nenašli sa žiadne partie za čierneho",

        // Endgame Trainer
        ["Endgame_WhiteToMove"] = "Biely na ťahu",
        ["Endgame_BlackToMove"] = "Čierny na ťahu",
        ["Endgame_TargetWin"] = "Cieľ: Výhra za {0}",
        ["Endgame_TargetDraw"] = "Cieľ: Remíza za {0}",
        ["Endgame_TargetWinShort"] = "Výhra za {0}",
        ["Endgame_TargetDrawShort"] = "Remíza za {0}",
        ["Endgame_OpponentThinking"] = "Súper premýšľa...",
        ["Endgame_ClassicalEndgames"] = "🏆 Klasické koncovky",
        ["Endgame_Mastered"] = "Zvládnuté: {0} / {1} ({2}%)",
        ["Endgame_Cat_All"] = "Všetky",
        ["Endgame_Cat_Pawns"] = "Pešiaci",
        ["Endgame_Cat_Rooks"] = "Veže",
        ["Endgame_Cat_Queens"] = "Dámy",
        ["Endgame_Cat_MinorPieces"] = "Ľahké figúry",
        ["Endgame_Cat_Practical"] = "Praktické",
        ["Endgame_KeySquares"] = "Kľúčové polia:",
        ["Endgame_Reset"] = "↻ Reset",
        ["Endgame_Hint"] = "💡 Nápoveda",
        ["Endgame_HideHint"] = "Skryť nápovedu",
        ["Endgame_Tablebase"] = "⚡ Syzygy tabuľky",
        ["Endgame_HideMoves"] = "Skryť ťahy",
        ["Endgame_Next"] = "Ďalej ▶",
        ["Endgame_TbCandidates"] = "⚡ Kandidátske ťahy zo Syzygy",

        // Puzzle Trainer
        ["Puzzle_Loading"] = "Načítavam {0}...",
        ["Puzzle_EstRating"] = "🎯 Odhad. rating: {0}",
        ["Puzzle_NumOf"] = "#{0} z {1}",
        ["Puzzle_WhiteToMove"] = "Biely na ťahu",
        ["Puzzle_BlackToMove"] = "Čierny na ťahu",
        ["Puzzle_NoBlundersYet"] = "Zatiaľ žiadne vlastné hrubky",
        ["Puzzle_NoBlundersDesc"] = "Caissalytics vytvára tréningové úlohy z partií, kde ste spravili chybu. Synchronizujte si online partie alebo ich analyzujte Stockfishom a kliknite na tlačidlo nižšie.",
        ["Puzzle_ReviewQueueClear"] = "Opakovací rad je prázdny!",
        ["Puzzle_ReviewQueueDesc"] = "Úlohy, v ktorých spravíte chybu, sa automaticky uložia sem na neskoršie precvičenie.",
        ["Puzzle_NoPuzzles"] = "Nenašli sa žiadne rébusy",
        ["Puzzle_NoPuzzlesDesc"] = "Skúste vybrať inú kategóriu vyššie alebo prehľadať databázu.",
        ["Puzzle_Rating"] = "Rating rébusov",
        ["Puzzle_CurrentStreak"] = "Aktuálna séria",
        ["Puzzle_Solved"] = "Vyriešené",
        ["Puzzle_Accuracy"] = "Úspešnosť",
        ["Puzzle_Mode_All"] = "Všetky",
        ["Puzzle_Mode_Blunders"] = "Moje hrubky",
        ["Puzzle_Mode_Master"] = "Majstrovská taktika",
        ["Puzzle_Mode_Review"] = "Opakovanie",
        ["Puzzle_YourRealGame"] = "Vaša skutočná partia",
        ["Puzzle_YourTurn"] = "Ste na ťahu! Nájdite najlepší ťah za {0}",
        ["Puzzle_White"] = "bieleho",
        ["Puzzle_Black"] = "čierneho",
        ["Puzzle_GoodMoveContinuation"] = "Dobrý ťah! Teraz nájdite pokračovanie...",
        ["Puzzle_SolvedBonus"] = "🎉 Vyriešené! +15 Rating",
        ["Puzzle_PlayedBlunder"] = "⚠️ Zahrali ste chybu z pôvodnej partie!",
        ["Puzzle_WrongMove"] = "❌ Nie celkom najlepší ťah.",
        ["Puzzle_WrongMoveDesc"] = "Skúste to znova alebo si pozrite správne riešenie.",
        ["Puzzle_SolutionRevealed"] = "💡 Odhalené riešenie",
        ["Puzzle_WinningMoves"] = "Víťazné ťahy: {0}",
        ["Puzzle_NextPuzzle"] = "Ďalší rébus",
        ["Puzzle_Solution"] = "Riešenie",
        ["Puzzle_Reset"] = "Reset",
        ["Puzzle_Skip"] = "Preskočiť",
        ["Puzzle_Analyze"] = "Analyzovať",
        ["Puzzle_ExtractTitle"] = "Získať moje hrubky",
        ["Puzzle_ExtractDesc"] = "Automaticky prehľadá vaše synchronizované partie v databáze a vytvorí tréningové úlohy z vašich chýb.",
        ["Puzzle_ScanBtn"] = "🔍 Prehľadať partie na chyby",
        ["Puzzle_Scanning"] = "⏳ Prehľadávam partie...",

        // Opening Tree & Repertoire
        ["Rep_Source"] = "Zdroj:",
        ["Rep_LocalDbs"] = "Lokálne databázy",
        ["Rep_OnlineExplorer"] = "Online prieskumník (Zadarmo)",
        ["Rep_AllMoves"] = "Všetky ťahy",
        ["Rep_WhiteRep"] = "Repertoár za bieleho",
        ["Rep_BlackRep"] = "Repertoár za čierneho",
        ["Rep_CandidateMoves"] = "Kandidátske ťahy",
        ["Rep_GamesCount"] = "{0} partií",
        ["Rep_Move"] = "Ťah",
        ["Rep_Games"] = "Partie",
        ["Rep_Freq"] = "Časť",
        ["Rep_Score"] = "Úspešnosť",
        ["Rep_Distribution"] = "Distribúcia",
        ["Rep_AvgElo"] = "Priem. Elo",
        ["Rep_Years"] = "Roky",
        ["Rep_TopGames"] = "Top partie dosahujúce pozíciu",
        ["Rep_MoveStatus"] = "Stav ťahu",
        ["Rep_MainLine"] = "Hlavný variant (Primárne odporúčanie)",
        ["Rep_Alternative"] = "Alternatíva (Spoľahlivá druhá voľba)",
        ["Rep_Surprise"] = "Prekvapenie (Taktický / bleskový variant)",
        ["Rep_PersonalNotes"] = "Osobné poznámky a príprava",
        ["Rep_NotesPlaceholder"] = "Zadajte taktické myšlienky, plány alebo varovania pre tento ťah...",
        ["Rep_RemoveFromRep"] = "Odstrániť z repertoáru",
        ["Rep_SaveMove"] = "Uložiť ťah",
        ["Rep_LichessTokenRequired"] = "Vyžaduje sa Lichess token",
        ["Rep_LichessTokenDesc"] = "Lichess vyžaduje bezplatný osobný token pre dopytovanie štatistík otvorení. Môžete si ho bezplatne vytvoriť na Lichess za pár sekúnd.",
        ["Rep_ConfigureInSettings"] = "⚙️ Nastaviť v Riadiacom centre",
        ["Rep_GetFreeToken"] = "🔗 Získať bezplatný token ↗",
        ["Rep_OfflineHint"] = "💡 Tip: Lokálne databázy (TWIC, Extraliga) nevyžadujú token a fungujú 100% offline!",
        ["Rep_NoRecordedMoves"] = "Z tejto pozície nie sú zaznamenané žiadne ťahy.",
        ["Rep_NoRecordedGames"] = "Z tejto pozície nie sú zaznamenané žiadne partie.",
        ["Rep_LoadingStats"] = "Načítavajú sa štatistiky variantov...",
        ["Rep_DisplayedCount"] = "{0} zobrazených",
        ["Rep_ColRep"] = "Rep",
        ["Rep_InRepertoire"] = "V repertoári",
        ["Rep_ClickToEdit"] = "Kliknutím upravte poznámku",
        ["Rep_AddToRepertoire"] = "Pridať do repertoáru",
        ["Rep_ModalTitle"] = "Repertoár",
        ["Rep_Main_Badge"] = "Hlavný variant",
        ["Rep_Alt_Badge"] = "Alternatíva",
        ["Rep_Surprise_Badge"] = "Prekvapenie",
        ["Rep_MyLines"] = "Moje debutové varianty",
        ["Rep_SaveLine"] = "Uložiť ako debutový variant",
        ["Rep_LineName"] = "Názov otvorenia",
        ["Rep_LineNamePlaceholder"] = "napr. Španielska hra, hlavný variant",
        ["Rep_LineDesc"] = "Popis (voliteľné)",
        ["Rep_LineDescPlaceholder"] = "Kľúčové myšlienky, poradie ťahov, typické plány...",
        ["Rep_LineColor"] = "Farba",
        ["Rep_LineSave"] = "Uložiť variant",
        ["Rep_LineDelete"] = "Zmazať variant",
        ["Rep_LineDeleteConfirm"] = "Zmazať tento debutový variant?",
        ["Rep_LineMoves"] = "{0} ťahov",
        ["Rep_NoLines"] = "Zatiaľ žiadne uložené debutové varianty.",
        ["Rep_NoLinesHint"] = "Zahrajte ťahy na šachovnici a kliknite na 'Uložiť ako debutový variant' pre vybudovanie repertoáru.",
        ["Rep_TrainLine"] = "Trénovať",
        ["Rep_TrainTitle"] = "Tréning: {0}",
        ["Rep_TrainYourMove"] = "Váš ťah!",
        ["Rep_TrainCorrect"] = "Správne!",
        ["Rep_TrainWrong"] = "Nesprávne! Správny ťah bol {0}.",
        ["Rep_TrainComplete"] = "Tréning dokončený!",
        ["Rep_TrainScore"] = "{0} / {1} správnych",
        ["Rep_TrainAgain"] = "Trénovať znova",
        ["Rep_TrainExit"] = "Ukončiť tréning",
        ["Rep_TrainProgress"] = "Pozícia {0} z {1}",
        ["Rep_TrainSkip"] = "Zobraziť odpoveď",
        ["Rep_ReplayLine"] = "Prehrať",
        ["General_Unknown"] = "Neznáme",

        // Board & Chessground Controls
        ["Board_FirstMove"] = "Prvý ťah",
        ["Board_PrevMove"] = "Predchádzajúci ťah",
        ["Board_NextMove"] = "Nasledujúci ťah",
        ["Board_LastMove"] = "Posledný ťah",
        ["Board_FlipBoard"] = "Otočiť šachovnicu",
        ["Board_You"] = "Vy",
        ["Board_Promotion_Queen"] = "Dáma",
        ["Board_Promotion_Rook"] = "Veža",
        ["Board_Promotion_Bishop"] = "Strelec",
        ["Board_Promotion_Knight"] = "Jazdec",

        // Board Editor
        ["BoardEditor_Title"] = "Editor šachovnice",
        ["BoardEditor_EditPosition"] = "Upraviť pozíciu",
        ["BoardEditor_StartingPos"] = "Základné postavenie",
        ["BoardEditor_ClearBoard"] = "Vyčistiť šachovnicu",
        ["BoardEditor_InvertColors"] = "Prehodiť farby",
        ["BoardEditor_PiecePalette"] = "Paleta figúrok",
        ["BoardEditor_WhitePieces"] = "Biele figúrky",
        ["BoardEditor_BlackPieces"] = "Čierne figúrky",
        ["BoardEditor_EraseSquare"] = "Nástroj mazania",
        ["BoardEditor_RightClickHint"] = "Tip: Ľavým kliknutím položíte figúrku, pravým zmažete pole",
        ["BoardEditor_PositionRules"] = "Pravidlá pozície",
        ["BoardEditor_SideToMove"] = "Ťah na ťahu",
        ["BoardEditor_CastlingRights"] = "Práva na rošádu",
        ["BoardEditor_LoadFen"] = "Načítať FEN",
        ["BoardEditor_CopyFen"] = "Kopírovať FEN",
        ["BoardEditor_PositionValid"] = "Pozícia je platná",
        ["BoardEditor_NoWhiteKing"] = "Pozícia musí obsahovať presne jedného bieleho kráľa (chýba).",
        ["BoardEditor_MultipleWhiteKings"] = "Pozícia musí obsahovať presne jedného bieleho kráľa (nájdených viacero).",
        ["BoardEditor_NoBlackKing"] = "Pozícia musí obsahovať presne jedného čierneho kráľa (chýba).",
        ["BoardEditor_MultipleBlackKings"] = "Pozícia musí obsahovať presne jedného čierneho kráľa (nájdených viacero).",
        ["BoardEditor_PawnsOnBackRank"] = "Pešiaci sa nemôžu nachádzať na 1. ani 8. rade.",
        ["BoardEditor_KingsAdjacent"] = "Králi nemôžu stáť na susedných poliach.",

        // Printable Homework & Diagrams
        ["Homework_Title"] = "Pracovné listy a diagramy",
        ["Homework_Subtitle"] = "Vytvárajte, upravujte a tlačte šachové pracovné listy pre zverencov a tréningové hodiny.",
        ["Homework_NewSheet"] = "Nový pracovný list",
        ["Homework_DiagramsCount"] = "diagramov",
        ["Homework_PerRow"] = "v rade",
        ["Homework_NoSheets"] = "Zatiaľ nie sú vytvorené žiadne pracovné listy",
        ["Homework_NoSheetsDesc"] = "Vytvorte prázdny pracovný list alebo si vyberte z trénerských šablón.",
        ["Homework_CreateFirst"] = "Vytvoriť prvý list",
        ["Homework_StudentHeader"] = "Hlavička pre študenta",
        ["Homework_AnswerKeyBadge"] = "Kľúč odpovedí",
        ["Homework_Edit"] = "Upraviť list",
        ["Homework_PrintPreview"] = "Náhľad tlače",
        ["Homework_Print"] = "Tlačiť / Exportovať PDF",
        ["Homework_Duplicate"] = "Duplikovať",
        ["Homework_Delete"] = "Zmazať",
        ["Homework_AllSheets"] = "Všetky listy",
        ["Homework_SheetTitle"] = "Názov pracovného listu",
        ["Homework_Instructions"] = "Inštrukcie / Podnadpis",
        ["Homework_CoachName"] = "Meno trénera",
        ["Homework_ClubName"] = "Šachový klub / Škola",
        ["Homework_DiagramsPerRow"] = "Počet diagramov v rade",
        ["Homework_Columns"] = "stĺpce",
        ["Homework_ShowCoordinates"] = "Zobraziť súradnice šachovnice",
        ["Homework_ShowSolutionLines"] = "Zobraziť riadky na písanie riešenia",
        ["Homework_IncludeAnswerKey"] = "Zahrnúť kľúč správnych odpovedí (strana 2)",
        ["Homework_DiagramsList"] = "Diagramy a úlohy",
        ["Homework_DiagramsListDesc"] = "Pridávajte, meňte poradie alebo upravujte pozície, zadania a riešenia.",
        ["Homework_ImportFromAnalysis"] = "Importovať z aktívnej analýzy",
        ["Homework_AddDiagram"] = "Pridať prázdny diagram",
        ["Homework_Orientation"] = "Orientácia",
        ["Homework_ExercisePrompt"] = "Zadanie úlohy",
        ["Homework_Solution"] = "Riešenie",
        ["Homework_TeacherKey"] = "Trénerský kľúč",
        ["Homework_BackToEditor"] = "Späť do editora",
        ["Homework_StudentName"] = "Meno",
        ["Homework_Date"] = "Dátum",
        ["Homework_Score"] = "Hodnotenie",
        ["Homework_AnswerKeyTitle"] = "Kľúč správnych odpovedí",
        ["Homework_AnswerKeyDesc"] = "Oficiálne riešenia a trénerský kľúč na vyhodnotenie.",
        ["Homework_DeleteConfirmTitle"] = "Zmazať pracovný list",
        ["Homework_DeleteConfirmDesc"] = "Naozaj chcete zmazať '{0}'? Túto akciu nemožno vrátiť späť."
    };
}
