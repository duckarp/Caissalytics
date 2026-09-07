using System.Net.Http.Json;
using System.Text.Json;
using Caissalytics.Core;

namespace Caissalytics.Data;

public class LichessExplorerClient
{
    private readonly HttpClient _httpClient;

    public LichessExplorerClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Caissalytics-Desktop/1.0");
        }
    }

    public async Task<PositionReferenceResult?> QueryAsync(string fen, bool isMasters = true, CancellationToken ct = default)
    {
        try
        {
            string cleanFen = Uri.EscapeDataString(fen.Trim());
            string url = isMasters
                ? $"https://explorer.lichess.ovh/masters?fen={cleanFen}&moves=12&topGames=15"
                : $"https://explorer.lichess.ovh/lichess?fen={cleanFen}&ratings=1600,1800,2000,2200&speeds=blitz,rapid,classical&moves=12&topGames=15";

            using var resp = await _httpClient.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var data = await resp.Content.ReadFromJsonAsync<LichessExplorerResponse>(cancellationToken: ct);
            if (data == null) return null;

            var result = new PositionReferenceResult
            {
                TotalPositionGames = data.White + data.Draws + data.Black
            };

            foreach (var m in data.Moves)
            {
                int moveTotal = m.White + m.Draws + m.Black;
                double freq = result.TotalPositionGames > 0 ? (moveTotal * 100.0 / result.TotalPositionGames) : 0;

                var stat = new PositionMoveStat
                {
                    MoveSan = m.San,
                    MoveUci = m.Uci,
                    TotalGames = moveTotal,
                    WhiteWins = m.White,
                    Draws = m.Draws,
                    BlackWins = m.Black,
                    AvgRating = m.AverageRating,
                    FrequencyPct = freq
                };
                result.CandidateMoves.Add(stat);
            }

            foreach (var g in data.TopGames)
            {
                string res = g.Winner switch
                {
                    "white" => "1-0",
                    "black" => "0-1",
                    _ => "1/2-1/2"
                };

                result.TopGames.Add(new GameHeader
                {
                    White = g.White?.Name ?? "White",
                    Black = g.Black?.Name ?? "Black",
                    WhiteElo = g.White?.Rating,
                    BlackElo = g.Black?.Rating,
                    Result = res,
                    Date = g.Year.HasValue ? $"{g.Year}.??.??" : "????.??.??",
                    Event = isMasters ? "FIDE Master Event" : "Lichess Rated",
                    Site = !string.IsNullOrEmpty(g.Id) ? $"https://lichess.org/{g.Id}" : "lichess.org"
                });
            }

            return result;
        }
        catch
        {
            return null;
        }
    }
}
