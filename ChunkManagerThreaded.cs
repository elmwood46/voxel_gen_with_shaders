using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

public struct Voxel 
{
    public int id;
}

public partial class ChunkManagerThreaded : Node
{
    //This will contain all modified voxels, structures, whatnot for all chunks, and will effectively be our saving mechanism
    public ConcurrentDictionary<Vector3I, Dictionary<Vector3I, Voxel>> ModifiedVoxels = new();
    public ConcurrentDictionary<Vector3I, Chunk> ActiveChunks = new();
    public Queue<Chunk> ChunkPool = new();
    ConcurrentQueue<Vector3I> ChunksToCreate = new();
    ConcurrentQueue<Vector3I> InactiveChunks = new();

    [ExportCategory("World Settings")]
    [Export (PropertyHint.Range, "1,32,")] public int RenderDistance {get;set;} = 8;
    [Export (PropertyHint.Range, "1,16,")] public int YRenderDistance {get;set;} = 1;
    [Export (PropertyHint.Range, "1,12,")] public int MaxChunksToProcessPerFrame {get;set;} = 6;
    int mainThreadID;
    private Thread _checkActiveChunksThread;
    bool performedFirstPass = false;
    private object _playerPositionLock = new(); 
    private Vector3 _playerPosition;

    private static readonly PackedScene _chunk_scene = ResourceLoader.Load<PackedScene>("res://chunk_scene.tscn");

    public override void _Ready()
    {
        InitializeWorld();
    }

    public override void _Process(double delta)
    {
        Vector3I chunk_pos_var;
        while (!InactiveChunks.IsEmpty && InactiveChunks.TryDequeue(out chunk_pos_var))
        {
            DeactivateChunk(chunk_pos_var);
        }

        for (int x = 0; x < MaxChunksToProcessPerFrame; x++)
        {
            if (x < MaxChunksToProcessPerFrame&& !ChunksToCreate.IsEmpty && ChunksToCreate.TryDequeue(out chunk_pos_var))
            {
                var chunk = GetOrGenerateChunk(chunk_pos_var);
                chunk.ChunkPosition = chunk_pos_var;
                ActiveChunks.TryAdd(chunk_pos_var, chunk);
                ComputeManager.Instance.GenerateVoxelData(chunk, chunk_pos_var);
                x++;
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        lock (_playerPositionLock)
        {
            _playerPosition = SimpleController.Instance.GlobalPosition;
        }
    }

    private void InitializeWorld()
    {

        var renderSizePlusExcess = RenderDistance + 3;
        var totalContainers = renderSizePlusExcess * renderSizePlusExcess * (YRenderDistance + 3);

        mainThreadID = Thread.CurrentThread.ManagedThreadId;

        for (int i = 0; i < totalContainers; i++)
        {
            GenerateChunk(Vector3I.Zero, true);
        }
        _checkActiveChunksThread = new Thread(ThreadProcess_CheckActiveChunksLoop)
        {
            Priority = ThreadPriority.BelowNormal
        };
        _checkActiveChunksThread.Start(); 

    }

    void ThreadProcess_CheckActiveChunksLoop()
    {
        int halfRenderSize = RenderDistance / 2;

        while (IsInstanceValid(this))
        {
            int playerChunkX, playerChunkZ;
            lock(_playerPositionLock)
            {
                playerChunkX = Mathf.FloorToInt(_playerPosition.X / ChunkManager.CHUNK_SIZE);
                playerChunkZ = Mathf.FloorToInt(_playerPosition.Z / ChunkManager.CHUNK_SIZE);
            }
            
            HashSet<Vector3I> chunks_checked = [];
            for (int x = -halfRenderSize; x < halfRenderSize; x++)
            {
                for (int z = -halfRenderSize; z < halfRenderSize; z++)
                {
                    for (int y = 0; y < YRenderDistance; y++)
                    {
                        var chunk_pos = new Vector3I(playerChunkX + x, y, playerChunkZ + z);
                        if (!ActiveChunks.ContainsKey(chunk_pos) && !ChunksToCreate.Contains(chunk_pos))
                        {
                            ChunksToCreate.Enqueue(chunk_pos);
                        }
                        chunks_checked.Add(chunk_pos);
                    }
                }
            }

            foreach (var (chunk_pos, _) in ActiveChunks)
            {
                if (!chunks_checked.Contains(chunk_pos)) InactiveChunks.Enqueue(chunk_pos);
            }

            if (!performedFirstPass) performedFirstPass = true;

            Thread.Sleep(300);
        }
    }

    #region Chunk Pooling
    public Chunk GetOrGenerateChunk(Vector3I pos)
    {
        if(ChunkPool.Count > 0)
        {
            return ChunkPool.Dequeue();
        }
        else
        {
            return GenerateChunk(pos, false);
        }
    }

    Chunk GenerateChunk(Vector3I position, bool enqueue)
    {
        if(System.Environment.CurrentManagedThreadId != mainThreadID)
        {
            ChunksToCreate.Enqueue(position);
            return null;
        }

        var chunk = _chunk_scene.Instantiate() as Chunk;
        
        Callable.From(() => {
            AddSibling(chunk);
            chunk.SetGlobalPosition((Vector3)position * Chunk.CHUNK_SIZE);
        }).CallDeferred();

        if (enqueue)
        {
            chunk.Deactivate();
            ChunkPool.Enqueue(chunk);
        }

        return chunk;
    }

    public bool DeactivateChunk(Vector3I position)
    {
        if (ActiveChunks.ContainsKey(position))
        {
            if (ActiveChunks.TryRemove(position, out Chunk c))
            {
                c.Deactivate();
                ChunkPool.Enqueue(c);
                return true;
            }
            else
                return false;
        }

        return false;
    }
    #endregion

    public static Vector3I PositionToChunkCoord(Vector3 global_position)
    {
        return new Vector3I(Mathf.FloorToInt(global_position.X / Chunk.CHUNK_SIZE), Mathf.FloorToInt(global_position.Y / Chunk.CHUNK_SIZE), Mathf.FloorToInt(global_position.Z / Chunk.CHUNK_SIZE));
    }
}