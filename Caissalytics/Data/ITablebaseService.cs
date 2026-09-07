using Caissalytics.Core;

namespace Caissalytics.Data;

public interface ITablebaseService
{
    string? LocalSyzygyPath { get; set; }
    bool IsOnlineProbeEnabled { get; set; }
    event Action? OnSettingsChanged;

    bool CanProbePosition(BoardPosition position);
    bool CanProbeFen(string fen);
    int CountPieces(BoardPosition position);

    Task<TablebaseResult?> ProbePositionAsync(string fen, CancellationToken ct = default);
    Task<TablebaseResult?> ProbePositionAsync(BoardPosition position, CancellationToken ct = default);
}
