using System.Collections.Generic;
using Godot;

public class CantorPairing
{
    private readonly HashSet<uint> _used = [];
    private readonly object _lock = new();
    public static CantorPairing Instance { get; } = new();
    private static uint MapToNatural(int n) => n >= 0 ? (uint)(2 * n) : (uint)(-2 * n - 1);
    private static uint Pair2D(uint a, uint b) => ((a + b) * (a + b + 1)) / 2 + b;
    private static uint Pair3D(uint a, uint b, uint c) => Pair2D(Pair2D(a, b), c);

    public static bool Contains(Vector3I coords)
    {
        lock (Instance._lock)
        {
            return Instance._used.Contains(GetCantorNumber(coords));
        }
    }

    public static HashSet<uint> GetSet() => Instance._used;

    public static void Clear()
    {
        lock (Instance._lock)
        {
            Instance._used.Clear();
        }
    }

    public static void Add(Vector3I coords)
    {
        lock (Instance._lock)
        {
            Instance._used.Add(GetCantorNumber(coords));
        }
    }

    public static void Add(uint cantor)
    {
        lock (Instance._lock)
        {
            Instance._used.Add(cantor);
        }
    }

    public static uint GetCantorNumber(Vector3I coords)
    {
        return Pair3D(MapToNatural(coords.X), MapToNatural(coords.Y), MapToNatural(coords.Z));
    }
}