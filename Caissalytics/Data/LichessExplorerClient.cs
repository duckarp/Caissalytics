using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Caissalytics.Core;

namespace Caissalytics.Data;

public class LichessExplorerClient
{
    private readonly HttpClient _httpClient;

    public LichessExplorerClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<PositionReferenceResult?> QueryAsync(string fen, bool isMasters = true, string? apiToken = null, CancellationToken ct = default)
    {
        try
        {
            string cleanFen = Uri.EscapeDataString(fen.Trim());
            string url = isMasters
                ? $"https://explorer.lichess.ovh/masters?fen={cleanFen}&moves=12&topGames=15"
                : $"https://explorer.lichess.ovh/lichess?fen={cleanFen}&ratings=1600,1800,2000,2200&speeds=blitz,rapid,classical&moves=12&topGames=15";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("Caissalytics-Desktop/1.0");

            if (!string.IsNullOrWhiteSpace(apiToken))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());
            }

            using var resp = await _httpClient.SendAsync(req, ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new PositionReferenceResult
                {
                    IsUnauthorized = true,
                    ErrorMessage = "Lichess Opening Explorer now requires a free API token due to recent bot protection policies. Add your free Personal Access Token in Settings -> Profile & Handles."
                };
            }

            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                return new PositionReferenceResult
                {
                    ErrorMessage = "Lichess Explorer rate limit reached. Please wait a few seconds and try again."
                };
            }

            if (!resp.IsSuccessStatusCode)
            {
                return new PositionReferenceResult
                {
                    ErrorMessage = $"Lichess Explorer returned HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase})."
                };
            }

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
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            return new PositionReferenceResult
            {
                ErrorMessage = $"Unable to reach Lichess Explorer: {ex.Message}"
            };
        }
    }
}
