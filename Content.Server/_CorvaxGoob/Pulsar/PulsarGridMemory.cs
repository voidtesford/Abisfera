namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Pulsar's memory map of one grid: 16x16 chunks with one byte per tile (<see cref="PulsarTileKind"/>,
/// 0 = never seen) and a 256-bit "seen by a camera in the last sweep" mask. About 300 bytes per chunk,
/// so a whole station is a few tens of KB. Doors are kept separately with their access.
/// </summary>
public sealed class PulsarGridMemory
{
    public const int ChunkSize = 16;
    public const int ChunkArea = ChunkSize * ChunkSize;

    [ViewVariables]
    public readonly Dictionary<Vector2i, PulsarChunk> Chunks = new();

    [ViewVariables]
    public readonly Dictionary<Vector2i, PulsarDoorInfo> Doors = new();

    /// <summary>Window origins of the current sweep (grid-local tile indices).</summary>
    public readonly List<Vector2i> Windows = new();

    [ViewVariables]
    public int WindowCursor;

    /// <summary>Completed sweeps over the whole grid.</summary>
    [ViewVariables]
    public int Sweeps;

    [ViewVariables]
    public int KnownTiles;

    [ViewVariables]
    public int VisibleTiles;

    /// <summary>Stage 5.2: tiles a linked unit sees right now (its own sensors), on top of the cameras.</summary>
    [ViewVariables]
    public int UnitSeenTiles;

    /// <summary>Stage 5.2: hazards the units' sensors found (low pressure, fire), with when they were last seen.</summary>
    [ViewVariables]
    public readonly Dictionary<Vector2i, (PulsarHazardKind Kind, TimeSpan Seen)> Hazards = new();

    /// <summary>Stage 5.2: tiles a unit could not get through (debris, a machine…), with when it found out.</summary>
    [ViewVariables]
    public readonly Dictionary<Vector2i, TimeSpan> Blocked = new();

    public static Vector2i ChunkIndex(Vector2i tile)
    {
        return new Vector2i(FloorDiv(tile.X), FloorDiv(tile.Y));
    }

    private static int FloorDiv(int v)
    {
        return v >= 0 ? v / ChunkSize : (v - ChunkSize + 1) / ChunkSize;
    }

    private static int LocalIndex(Vector2i tile)
    {
        var x = tile.X - FloorDiv(tile.X) * ChunkSize;
        var y = tile.Y - FloorDiv(tile.Y) * ChunkSize;
        return y * ChunkSize + x;
    }

    public PulsarTileKind GetTile(Vector2i tile)
    {
        return Chunks.TryGetValue(ChunkIndex(tile), out var chunk)
            ? (PulsarTileKind) chunk.Tiles[LocalIndex(tile)]
            : PulsarTileKind.Unknown;
    }

    public bool IsVisible(Vector2i tile)
    {
        if (!Chunks.TryGetValue(ChunkIndex(tile), out var chunk))
            return false;

        var i = LocalIndex(tile);
        return (chunk.Visible[i >> 6] & (1UL << (i & 63))) != 0;
    }

    public bool IsUnitSeen(Vector2i tile)
    {
        if (!Chunks.TryGetValue(ChunkIndex(tile), out var chunk))
            return false;

        var i = LocalIndex(tile);
        return (chunk.UnitSeen[i >> 6] & (1UL << (i & 63))) != 0;
    }

    /// <summary>Seen by a camera in the last sweep or by a linked unit right now.</summary>
    public bool IsSeen(Vector2i tile)
    {
        return IsVisible(tile) || IsUnitSeen(tile);
    }

    public void SetUnitSeen(Vector2i tile)
    {
        var chunk = GetOrAddChunk(ChunkIndex(tile));
        var i = LocalIndex(tile);
        var bit = 1UL << (i & 63);
        if ((chunk.UnitSeen[i >> 6] & bit) != 0)
            return;
        chunk.UnitSeen[i >> 6] |= bit;
        UnitSeenTiles++;
    }

    public void ClearUnitSeen()
    {
        if (UnitSeenTiles == 0)
            return;
        foreach (var chunk in Chunks.Values)
            Array.Clear(chunk.UnitSeen);
        UnitSeenTiles = 0;
    }

    public void SetTile(Vector2i tile, PulsarTileKind kind, TimeSpan now)
    {
        var chunk = GetOrAddChunk(ChunkIndex(tile));
        var i = LocalIndex(tile);
        if (chunk.Tiles[i] == 0 && kind != PulsarTileKind.Unknown)
            KnownTiles++;
        chunk.Tiles[i] = (byte) kind;
        chunk.LastSeen = now;
    }

    public void SetVisible(Vector2i tile, bool visible)
    {
        var chunk = GetOrAddChunk(ChunkIndex(tile));
        var i = LocalIndex(tile);
        var bit = 1UL << (i & 63);
        var was = (chunk.Visible[i >> 6] & bit) != 0;
        if (was == visible)
            return;

        if (visible)
        {
            chunk.Visible[i >> 6] |= bit;
            VisibleTiles++;
        }
        else
        {
            chunk.Visible[i >> 6] &= ~bit;
            VisibleTiles--;
        }
    }

    public void ClearVisible()
    {
        foreach (var chunk in Chunks.Values)
            Array.Clear(chunk.Visible);
        VisibleTiles = 0;
    }

    private PulsarChunk GetOrAddChunk(Vector2i index)
    {
        if (!Chunks.TryGetValue(index, out var chunk))
        {
            chunk = new PulsarChunk();
            Chunks[index] = chunk;
        }

        return chunk;
    }
}

public sealed class PulsarChunk
{
    public readonly byte[] Tiles = new byte[PulsarGridMemory.ChunkArea];
    public readonly ulong[] Visible = new ulong[PulsarGridMemory.ChunkArea / 64];
    public readonly ulong[] UnitSeen = new ulong[PulsarGridMemory.ChunkArea / 64];

    [ViewVariables]
    public TimeSpan LastSeen;
}

public enum PulsarHazardKind : byte
{
    LowPressure,
    Fire,
}
