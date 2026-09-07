using System.Text.RegularExpressions;

namespace Caissalytics.Data;

public static class OpeningCatalog
{
    private static readonly Regex OpeningTagRegex = new(@"\[Opening\s+""([^""]+)""\]", RegexOptions.Compiled);
    private static readonly Regex VariationTagRegex = new(@"\[Variation\s+""([^""]+)""\]", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> EcoDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        // A: Flank openings, Indian defenses, Dutch
        ["A00"] = "Uncommon Opening",
        ["A01"] = "Nimzo-Larsen Attack",
        ["A02"] = "Bird's Opening",
        ["A03"] = "Bird's Opening",
        ["A04"] = "Réti Opening",
        ["A05"] = "Réti Opening: King's Indian Attack",
        ["A06"] = "Réti Opening",
        ["A07"] = "King's Indian Attack",
        ["A08"] = "King's Indian Attack",
        ["A09"] = "Réti Opening",
        ["A10"] = "English Opening",
        ["A11"] = "English: Caro-Kann Defensive System",
        ["A12"] = "English Opening",
        ["A13"] = "English Opening",
        ["A15"] = "English: Anglo-Indian",
        ["A16"] = "English: Anglo-Indian",
        ["A20"] = "English: King's English",
        ["A21"] = "English: King's English",
        ["A22"] = "English: Two Knights",
        ["A25"] = "English: Closed System",
        ["A30"] = "English: Symmetrical",
        ["A34"] = "English: Symmetrical",
        ["A40"] = "Queen's Pawn Game",
        ["A41"] = "Queen's Pawn Game",
        ["A42"] = "Modern Defense: Averbakh System",
        ["A43"] = "Old Benoni Defense",
        ["A45"] = "Trompowsky Attack",
        ["A46"] = "Torre Attack",
        ["A48"] = "King's Indian: East Indian Defense",
        ["A50"] = "Queen's Pawn Game: Black Knights' Tango",
        ["A51"] = "Budapest Gambit",
        ["A52"] = "Budapest Gambit",
        ["A53"] = "Old Indian Defense",
        ["A57"] = "Benko Gambit",
        ["A58"] = "Benko Gambit Accepted",
        ["A60"] = "Modern Benoni",
        ["A70"] = "Modern Benoni: Classical",
        ["A80"] = "Dutch Defense",
        ["A84"] = "Dutch Defense",
        ["A85"] = "Dutch: with c4 & Nc3",
        ["A90"] = "Dutch: Classical",

        // B: Semi-Open games (except French)
        ["B00"] = "King's Pawn Game",
        ["B01"] = "Scandinavian Defense",
        ["B02"] = "Alekhine's Defense",
        ["B03"] = "Alekhine's Defense: Four Pawns Attack",
        ["B06"] = "Modern Defense",
        ["B07"] = "Pirc Defense",
        ["B08"] = "Pirc: Classical",
        ["B09"] = "Pirc: Austrian Attack",
        ["B10"] = "Caro-Kann Defense",
        ["B11"] = "Caro-Kann: Two Knights",
        ["B12"] = "Caro-Kann: Advance Variation",
        ["B13"] = "Caro-Kann: Exchange Variation",
        ["B14"] = "Caro-Kann: Panov-Botvinnik Attack",
        ["B15"] = "Caro-Kann Defense",
        ["B18"] = "Caro-Kann: Classical",
        ["B19"] = "Caro-Kann: Classical",
        ["B20"] = "Sicilian Defense",
        ["B21"] = "Sicilian: Grand Prix / Morra Gambit",
        ["B22"] = "Sicilian Defense: Alapin Variation",
        ["B23"] = "Sicilian Defense: Closed",
        ["B24"] = "Sicilian Defense: Closed",
        ["B25"] = "Sicilian Defense: Closed",
        ["B26"] = "Sicilian Defense: Closed",
        ["B27"] = "Sicilian Defense: Hungarian / Mongoose",
        ["B28"] = "Sicilian: O'Kelly Variation",
        ["B29"] = "Sicilian: Nimzowitsch-Rossolimo Attack",
        ["B30"] = "Sicilian Defense: Old Sicilian",
        ["B31"] = "Sicilian: Rossolimo Attack",
        ["B32"] = "Sicilian: Kalashnikov / Labourdonnais",
        ["B33"] = "Sicilian: Sveshnikov (Lasker-Pelikan)",
        ["B34"] = "Sicilian: Accelerated Dragon",
        ["B35"] = "Sicilian: Accelerated Dragon",
        ["B40"] = "Sicilian: French Variation",
        ["B41"] = "Sicilian: Kan Variation",
        ["B44"] = "Sicilian: Taimanov Variation",
        ["B45"] = "Sicilian: Taimanov Variation",
        ["B50"] = "Sicilian Defense",
        ["B51"] = "Sicilian: Canal-Sokolsky Attack",
        ["B52"] = "Sicilian: Canal-Sokolsky Attack",
        ["B53"] = "Sicilian: Chekhover Variation",
        ["B54"] = "Sicilian Defense",
        ["B70"] = "Sicilian Defense: Dragon",
        ["B72"] = "Sicilian: Dragon",
        ["B75"] = "Sicilian: Dragon, Yugoslav Attack",
        ["B80"] = "Sicilian Defense: Scheveningen",
        ["B84"] = "Sicilian: Scheveningen, Classical",
        ["B90"] = "Sicilian Defense: Najdorf",
        ["B92"] = "Sicilian: Najdorf, Opocensky",
        ["B96"] = "Sicilian: Najdorf",
        ["B99"] = "Sicilian Defense: Najdorf, Main Line",

        // C: Open Games (1.e4 e5) & French Defense
        ["C00"] = "French Defense",
        ["C01"] = "French: Exchange Variation",
        ["C02"] = "French: Advance Variation",
        ["C03"] = "French: Tarrasch",
        ["C05"] = "French: Tarrasch, Closed",
        ["C10"] = "French: Paulsen Variation",
        ["C11"] = "French: Classical",
        ["C15"] = "French: Winawer",
        ["C20"] = "King's Pawn Game",
        ["C21"] = "Center Game / Danish Gambit",
        ["C22"] = "Center Game",
        ["C23"] = "Bishop's Opening",
        ["C24"] = "Bishop's Opening: Berlin Defense",
        ["C25"] = "Vienna Game",
        ["C26"] = "Vienna Game: Falkbeer",
        ["C28"] = "Vienna Game",
        ["C29"] = "Vienna Gambit",
        ["C30"] = "King's Gambit",
        ["C33"] = "King's Gambit Accepted",
        ["C34"] = "King's Gambit Accepted",
        ["C40"] = "King's Knight Opening",
        ["C41"] = "Philidor Defense",
        ["C42"] = "Petrov's Defense (Russian)",
        ["C43"] = "Petrov: Modern Attack",
        ["C44"] = "Scotch Opening / Ponziani",
        ["C45"] = "Scotch Game",
        ["C46"] = "Three Knights Opening",
        ["C47"] = "Four Knights Game",
        ["C48"] = "Four Knights: Spanish",
        ["C49"] = "Four Knights: Double Spanish",
        ["C50"] = "Italian Game",
        ["C51"] = "Evans Gambit",
        ["C52"] = "Evans Gambit",
        ["C53"] = "Giuoco Piano",
        ["C54"] = "Giuoco Piano",
        ["C55"] = "Two Knights Defense",
        ["C57"] = "Two Knights: Fried Liver Attack",
        ["C60"] = "Ruy Lopez (Spanish Opening)",
        ["C61"] = "Ruy Lopez: Bird's Defense",
        ["C62"] = "Ruy Lopez: Old Steinitz",
        ["C63"] = "Ruy Lopez: Schliemann Defense",
        ["C65"] = "Ruy Lopez: Berlin Defense",
        ["C68"] = "Ruy Lopez: Exchange Variation",
        ["C70"] = "Ruy Lopez",
        ["C77"] = "Ruy Lopez: Morphy Defense",
        ["C78"] = "Ruy Lopez: Archangelsk",
        ["C80"] = "Ruy Lopez: Open Variation",
        ["C84"] = "Ruy Lopez: Closed",
        ["C88"] = "Ruy Lopez: Closed",
        ["C92"] = "Ruy Lopez: Closed, Chigorin",

        // D: Closed & Semi-Closed Games
        ["D00"] = "Queen's Pawn Game",
        ["D02"] = "Queen's Pawn: London System",
        ["D04"] = "Queen's Pawn: Colle System",
        ["D06"] = "Queen's Gambit",
        ["D07"] = "Chigorin Defense",
        ["D10"] = "Slav Defense",
        ["D11"] = "Slav Defense: Modern Line",
        ["D15"] = "Slav Defense: Quiet Variation",
        ["D20"] = "Queen's Gambit Accepted",
        ["D25"] = "Queen's Gambit Accepted",
        ["D30"] = "Queen's Gambit Declined",
        ["D31"] = "Semi-Slav Defense",
        ["D35"] = "Queen's Gambit Declined: Exchange",
        ["D37"] = "Queen's Gambit Declined: 4.Nf3",
        ["D43"] = "Semi-Slav Defense",
        ["D45"] = "Semi-Slav: Stoltz Variation",
        ["D50"] = "Queen's Gambit Declined: 4.Bg5",
        ["D70"] = "Neo-Grünfeld Defense",
        ["D80"] = "Grünfeld Defense",
        ["D85"] = "Grünfeld: Exchange Variation",
        ["D90"] = "Grünfeld: Classical",

        // E: Indian Defenses
        ["E00"] = "Catalan Opening",
        ["E01"] = "Catalan: Closed",
        ["E06"] = "Catalan: Closed, 5.Nf3",
        ["E10"] = "Queen's Pawn: Blumenfeld / Bogo",
        ["E11"] = "Bogo-Indian Defense",
        ["E12"] = "Queen's Indian Defense",
        ["E15"] = "Queen's Indian: 4.g3",
        ["E20"] = "Nimzo-Indian Defense",
        ["E30"] = "Nimzo-Indian: Leningrad",
        ["E32"] = "Nimzo-Indian: Classical (Capablanca)",
        ["E40"] = "Nimzo-Indian: 4.e3",
        ["E50"] = "Nimzo-Indian: 4.e3 with ...c5",
        ["E60"] = "King's Indian Defense",
        ["E61"] = "King's Indian Defense",
        ["E70"] = "King's Indian: Classical / Fianchetto",
        ["E76"] = "King's Indian: Four Pawns Attack",
        ["E80"] = "King's Indian: Sämisch Variation",
        ["E90"] = "King's Indian: Classical",
        ["E97"] = "King's Indian: Classical, Aronin-Taimanov",
        ["E99"] = "King's Indian: Classical, Main Line"
    };

    public static string ResolveOpeningName(string? eco, string? pgn = null)
    {
        // 1. Try explicit [Opening "..."] tag in PGN
        if (!string.IsNullOrWhiteSpace(pgn))
        {
            var match = OpeningTagRegex.Match(pgn);
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                string opening = match.Groups[1].Value.Trim();
                var varMatch = VariationTagRegex.Match(pgn);
                if (varMatch.Success && !string.IsNullOrWhiteSpace(varMatch.Groups[1].Value))
                {
                    string variation = varMatch.Groups[1].Value.Trim();
                    if (!opening.Contains(variation, StringComparison.OrdinalIgnoreCase))
                    {
                        return $"{opening}: {variation}";
                    }
                }
                return opening;
            }
        }

        // 2. Lookup in standard ECO dictionary
        string cleanEco = (eco ?? "").Trim().ToUpperInvariant();
        if (cleanEco.Length >= 3 && EcoDictionary.TryGetValue(cleanEco[..3], out var exactName))
        {
            return exactName;
        }

        // 3. Fallback by ECO major family prefix
        if (cleanEco.Length >= 2)
        {
            string prefix = cleanEco[..2];
            switch (prefix)
            {
                case "A0": return "Flank Opening / Réti";
                case "A1":
                case "A2":
                case "A3": return "English Opening";
                case "A4": return "Queen's Pawn Game";
                case "A5": return "Old Indian / Benko Gambit";
                case "A6":
                case "A7": return "Modern Benoni";
                case "A8":
                case "A9": return "Dutch Defense";
                case "B0": return "Modern / Scandinavian / Pirc";
                case "B1": return "Caro-Kann Defense";
                case "B2":
                case "B3":
                case "B4":
                case "B5":
                case "B6":
                case "B7":
                case "B8":
                case "B9": return "Sicilian Defense";
                case "C0":
                case "C1": return "French Defense";
                case "C2": return "Open Game (1.e4 e5)";
                case "C3": return "King's Gambit";
                case "C4": return "Four Knights / Petrov";
                case "C5": return "Italian Game / Two Knights";
                case "C6":
                case "C7":
                case "C8":
                case "C9": return "Ruy Lopez";
                case "D0": return "Queen's Pawn Game";
                case "D1":
                case "D2":
                case "D3":
                case "D4":
                case "D5":
                case "D6": return "Queen's Gambit";
                case "D7":
                case "D8":
                case "D9": return "Grünfeld Defense";
                case "E0": return "Catalan Opening";
                case "E1": return "Queen's Indian Defense";
                case "E2":
                case "E3":
                case "E4":
                case "E5": return "Nimzo-Indian Defense";
                case "E6":
                case "E7":
                case "E8":
                case "E9": return "King's Indian Defense";
            }
        }

        return string.IsNullOrEmpty(cleanEco) ? "Unknown Opening" : $"Opening ({cleanEco})";
    }
}
