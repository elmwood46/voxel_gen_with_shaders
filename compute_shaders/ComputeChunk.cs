using Godot;
using Godot.NativeInterop;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

[GlobalClass]
public partial class NoiseLayer : Resource
{
    [Export] public float Gain = 0.7f;
    [Export] public float Frequency = 700f;
    [Export] public float Lacunarity = 3.3f;
    [Export] public float Persistence = 0.27f;
    [Export] public int Octaves = 5;
    [Export] public float CaveScale = 50f;
    [Export] public float CaveThreshold = 0.75f;
    [Export] public int SurfaceVoxelId = 3;
    [Export] public int SubSurfaceVoxelId = 2;
}

public static class ComputeChunk
{
    // struct to hold the parameters for the compute shader
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ChunkParamsStruct
    {
        public System.Numerics.Vector4 SeedOffset;
        public int CSP;
        public int CSP3;
        public int NumChunksToCompute;
        public int MaxWorldHeight;
        public int StoneBlockID;
        public int OceanHeight;
        public int NoiseLayerCount;
        public int NoiseSeed;
        public float NoiseScale;
        public float CaveNoiseScale;
        public float CaveThreshold;
        public bool GenerateCaves;
        public bool ForceFloor;
        private readonly int _padding1;
        private readonly int _padding2;
        private readonly int _padding3;
        
        // THIS FIELD IS ADDED SEPARATELY WHEN PARAMETERS ARE FILLED
        // it is a variable sized array, and cannot be blittable
        // avoid non-blittalbe type in struct, instead we append this to the buffer in GenerateParameterBufferBytes 
        // public System.Numerics.Vector3[] ChunkPositions;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct NoiseLayerStruct
    {
        public float Gain;
        public float Frequency;
        public float Lacunarity;
        public float Persistence;
        public int Octaves;
        public float CaveScale;
        public float CaveThreshold;
        public int SurfaceVoxelId;
        public int SubSurfaceVoxelId;
        private readonly int _padding1;
        private readonly int _padding2;
        private readonly int _padding3;

        public NoiseLayerStruct(){}

        public NoiseLayerStruct(NoiseLayer noiseLayer)
        {
            Gain = noiseLayer.Gain;
            Frequency = noiseLayer.Frequency;
            Lacunarity = noiseLayer.Lacunarity;
            Persistence = noiseLayer.Persistence;
            Octaves = noiseLayer.Octaves;
            CaveScale = noiseLayer.CaveScale;
            CaveThreshold = noiseLayer.CaveThreshold;
            SurfaceVoxelId = noiseLayer.SurfaceVoxelId;
            SubSurfaceVoxelId = noiseLayer.SubSurfaceVoxelId;
        }
    }

    private static readonly RDShaderFile _shader_file = ResourceLoader.Load<RDShaderFile>("res://compute_shaders/chunkgen.glsl");

    public static int MaxWorldHeight = 250;
    public static float CaveNoiseScale = 550.0f;
    public static float CaveThreshold = 0.75f;
    public static int OceanHeight = 64;
    public static bool GenerateCaves = true;
    public static bool ForceFloor = true;

    /*
            Gain = 0.5f,
            Frequency = 0.01f,
            Lacunarity = 2.0f,
            Persistence = 0.5f,
            Octaves = 5,
            CaveScale = 0.5f,
            CaveThreshold = 0.8f,
            SurfaceVoxelId = BlockManager.GrassBlockId,
            SubSurfaceVoxelId = BlockManager.DirtBlockId
    */
    /*
            Gain = 0.7f,
            Frequency = 700f,
            Lacunarity = 3.3f,
            Persistence = 0.27f,
            Octaves = 5,
            CaveScale = 550f,
            CaveThreshold = 0.75f,
            SurfaceVoxelId = BlockManager.GrassBlockId,
            SubSurfaceVoxelId = BlockManager.DirtBlockId
    */

    private static NoiseLayerStruct[] _noise_layers = [
        new NoiseLayerStruct
        {
            Gain = 0.7f,
            Frequency = 700f,
            Lacunarity = 3.3f,
            Persistence = 0.27f,
            Octaves = 5,
            CaveScale = 25f,
            CaveThreshold = 0.0f,
            SurfaceVoxelId = BlockManager.GrassBlockId,
            SubSurfaceVoxelId = BlockManager.DirtBlockId
        },
        new NoiseLayerStruct
        {
            Gain = 0.002f,
            Frequency = 0.005f,
            Lacunarity = 3.0f,
            Persistence = 0.5f,
            Octaves = 4,
            CaveScale = 0.5f,
            CaveThreshold = 0.8f,
            SurfaceVoxelId = BlockManager.GrassBlockId,
            SubSurfaceVoxelId = BlockManager.DirtBlockId
        }
    ];

    public static void SetNoiseLayer(int index,NoiseLayer noiseLayer)
    {
        _noise_layers[index] = new NoiseLayerStruct(noiseLayer);
    }

    private static ChunkParamsStruct input_params = new() 
    {
        SeedOffset = new(0.0f,0.0f,0.0f,0.0f),
        CSP = ChunkManager.CSP,
        CSP3 = ChunkManager.CSP3,
        NumChunksToCompute = 1,
        MaxWorldHeight = ChunkManager.CHUNK_SIZE*ChunkManager.Instance.YRenderDistance,
        StoneBlockID = BlockManager.StoneBlockId,
        OceanHeight = 64,
        NoiseLayerCount = 1,
        NoiseSeed = 0,
        NoiseScale = 1.0f,
        CaveNoiseScale = 550.0f,
        CaveThreshold = 0.8f,
        GenerateCaves = true,
        ForceFloor = true,
    };
    //private static readonly RenderingDevice _rd = RenderingServer.CreateLocalRenderingDevice();

    //public static ConcurrentDictionary<RenderingDevice,  int> _computeResources = new();

    /*
    private static void BufferGetDataCallback(byte[] array, Vector3I chunkPosition, RenderingDevice _rd, Rid[] rids)
    {
        _rd.Sync();
        GD.Print("Generated chunk at: " + chunkPosition);
        //_rd.Sync();
        var data = new int[ChunkManager.CSP3];
        Buffer.BlockCopy(array, 0, data, 0, array.Length);

        ChunkManager.BLOCKCACHE[chunkPosition] = data;
        var tempstring = "";
        for (int i = 0; i < 20; i++)
        {
            tempstring +=  ChunkManager.BLOCKCACHE[chunkPosition][i] + " ";
        }
        tempstring += "...";
        GD.Print(tempstring);
        
        RenderingServer.CallOnRenderThread(Callable.From(()=>
        {
            foreach (Rid r in rids)
            {
                _rd.FreeRid(r);
            }
            _rd.Free();            
        }));
    }*/

    public static void GenerateMultiChunks(List<Vector3I> chunksToGenerate)
    {
        var stopwatch = Stopwatch.StartNew();
        var _rd = RenderingServer.CreateLocalRenderingDevice();
        var _shader_spir_v = _shader_file.GetSpirV();
        
        var _compute_shader = _rd.ShaderCreateFromSpirV(_shader_spir_v);

        var _num_blocks = ChunkManager.CSP3*chunksToGenerate.Count;

        // due to invocations being 8x8x8, min padded chunk size is 8x8x8, and padded chunk size should always be a multiple of 8
        // a good padded chunk size is 32x32x32, which is divided between 4x4x4 workgroups:
        var workgroups = (uint) ChunkManager.CSP / 8;

        var _voxel_buffer_rid = GenerateIntBuffer(_rd, _num_blocks);
        var _voxel_array_uniform = GenerateUniform(_voxel_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 0);

        var paramsBufferBytes = GenerateParameterBufferBytes(chunksToGenerate);
        var _params_buffer_rid = _rd.StorageBufferCreate((uint)paramsBufferBytes.Length, paramsBufferBytes);
        var _params_uniform = GenerateUniform(_params_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 1);

        var noiseBufferBytes = GenerateNoiseLayerBufferBytes();
        var _noise_buffer_rid = _rd.StorageBufferCreate((uint)noiseBufferBytes.Length, noiseBufferBytes);
        var _noise_uniform = GenerateUniform(_noise_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 2);

        var atomicCounterBytes = new byte[sizeof(uint)];
        var _atomic_counter_rid = _rd.StorageBufferCreate((uint)atomicCounterBytes.Length, atomicCounterBytes);
        var _atomic_counter_uniform = GenerateUniform(_atomic_counter_rid, RenderingDevice.UniformType.StorageBuffer, 3);
        
        var test_buffer_size = 10;
        var test_buffer = new byte[test_buffer_size*sizeof(int)];
        var test_buffer_rid = _rd.StorageBufferCreate((uint)test_buffer.Length, test_buffer);
        var test_buffer_unifom = GenerateUniform(test_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 4);

        var _bindings = new Godot.Collections.Array<RDUniform> { _voxel_array_uniform, _params_uniform, _noise_uniform, _atomic_counter_uniform, test_buffer_unifom };

        var _uniform_set = _rd.UniformSetCreate(_bindings, _compute_shader, 0);

        GD.Print("Running compute shader");

        var _compute_pipeline = _rd.ComputePipelineCreate(_compute_shader);
        var _compute_list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(_compute_list, _compute_pipeline);
        _rd.ComputeListBindUniformSet(_compute_list, _uniform_set, 0);
        _rd.ComputeListDispatch(_compute_list, workgroups * (uint)chunksToGenerate.Count, workgroups, workgroups);
        //_rd.ComputeListAddBarrier(_compute_list);
        _rd.ComputeListEnd();
        _rd.Submit();

        var rids = new Rid[] {_uniform_set, _voxel_buffer_rid, _params_buffer_rid, _noise_buffer_rid, _atomic_counter_rid, _compute_pipeline, _compute_shader};

        _rd.Sync();
        var voxel_array_readback = _rd.BufferGetData(_voxel_buffer_rid);

        // DEBUG print information about world gen
        /*
        //GD.Print("array length: " + voxel_array_readback.Length);
        //GD.Print("array length/sizeof(int): " + voxel_array_readback.Length/sizeof(int));

        // Read back buffer from GPU
        byte[] params_bytes_readback = _rd.BufferGetData(_params_buffer_rid);

        // Deserialize back into struct
        var outputParams = MemoryMarshal.Read<ChunkParamsStruct>(params_bytes_readback);
        // Extract the float array (from structSize to end)
        var vec4_array = new System.Numerics.Vector4[outputParams.NumChunksToCompute];
        MemoryMarshal.Cast<byte, System.Numerics.Vector4>(params_bytes_readback.AsSpan(Marshal.SizeOf<ChunkParamsStruct>())).CopyTo(vec4_array);

        var test_buffer_readback = _rd.BufferGetData(test_buffer_rid);
        var int_array = new int[test_buffer_size];
        MemoryMarshal.Cast<byte, int>(test_buffer_readback.AsSpan()).CopyTo(int_array);
        GD.Print("test buffer readback: ", string.Join(",", int_array));

        GD.Print("readback chunk positions: ", string.Join(",\n", vec4_array));
        //ReadBackParamsBuffer(_rd, _params_buffer_rid);

        GD.Print("MemoryMarshal size of ChunkParamsStruct: " + Marshal.SizeOf<ChunkParamsStruct>());
        */

        bool allZeroes = true;
        var stride = Marshal.SizeOf<int>()*ChunkManager.CSP3;
        for (int i=0; i<chunksToGenerate.Count; i++)
        {
            var chunkData = new int[ChunkManager.CSP3];
            MemoryMarshal.Cast<byte, int>(voxel_array_readback.AsSpan(stride*i,stride)).CopyTo(chunkData);

            var chunkPosition = chunksToGenerate[i];
            ChunkManager.BLOCKCACHE[chunkPosition] = chunkData;
            if (!chunkData.All(x => x == 0)) allZeroes = false;
            else{
                //GD.Print("Chunk at " + chunkPosition + " is all zeroes");
            }
            //GD.Print("Generate chunk at: " + chunkPosition + chunkData.Take(10).ToArray().Join(",") + "...");
            //GD.Print("data zeroes: " + chunkData.Count(x => x == 0));
        }
        if (allZeroes) GD.Print("DATA IS ALL ZEROES");

        /*
        var mesh_set = ChunkManager.MESHCACHE.Keys.ToHashSet();
        var chunks_set = ChunkManager.BLOCKCACHE.Keys.ToHashSet();
        var exception = mesh_set.Except(chunks_set).ToList();
        GD.Print("exception betwen MESHCACHE and BLOCKCACHE: ", exception.Count);
        */

        stopwatch.Stop();

        //GD.Print($"Generated {chunksToGenerate.Count} chunks");
        GD.Print($"GenerateMultiChunk time elapsed: {stopwatch.ElapsedMilliseconds} ms");
        
        //ReadBackBuffer(_rd, _params_buffer);
        //ReadBackNoiseLayerBuffer(_rd, _noise_buffer_rid);

        /*
        for (int i=0; i < chunksToGenerate.Count; i++)
        {
            var chunkPosition = chunksToGenerate[i];
            var tempstring = "[";
            for (int j = 0; j < 10; j++)
            {
                tempstring += ChunkManager.BLOCKCACHE[chunkPosition][i] + ",";
            }
            tempstring += "...]";
            GD.Print("Generated chunk at: " + chunkPosition + tempstring);
        }
        */

        FreeRids(_rd, rids);
    }

    private static void ReadBackNoiseLayerBuffer(RenderingDevice _rd, Rid _storageBuffer)
    {
        // Read back buffer from GPU
        byte[] outputBytes = _rd.BufferGetData(_storageBuffer);

        // Deserialize back into struct
        var outputParams = MemoryMarshal.Read<NoiseLayerStruct>(outputBytes);
        // Extract the float array (from structSize to end)
        //var noise_layer_array = new NoiseLayerStruct[1];
        //MemoryMarshal.Cast<byte, NoiseLayerStruct>(outputBytes.AsSpan()[Marshal.SizeOf<NoiseLayerStruct>()..]).CopyTo(noise_layer_array);
        
        // Print values for debugging
        GD.Print("DEBUG: reading back noise layer buffer...");
        GD.Print($"Gain: {outputParams.Gain}");
        GD.Print($"Frequency: {outputParams.Frequency}");
        GD.Print($"Lacunarity: {outputParams.Lacunarity}");
        GD.Print($"Persistence: {outputParams.Persistence}");
        GD.Print($"Octaves: {outputParams.Octaves}");
        GD.Print($"CaveScale: {outputParams.CaveScale}");
        GD.Print($"CaveThreshold: {outputParams.CaveThreshold}");
        GD.Print($"SurfaceVoxelId: {outputParams.SurfaceVoxelId}");
        GD.Print($"SubSurfaceVoxelId: {outputParams.SubSurfaceVoxelId}");
    }

    private static void ReadBackParamsBuffer(RenderingDevice _rd, Rid _storageBuffer)
    {
        // Read back buffer from GPU
        byte[] outputBytes = _rd.BufferGetData(_storageBuffer);

        // Deserialize back into struct
        var outputParams = MemoryMarshal.Read<ChunkParamsStruct>(outputBytes);
        // Extract the float array (from structSize to end)
        var vec4_array = new System.Numerics.Vector4[outputParams.NumChunksToCompute];
        MemoryMarshal.Cast<byte, System.Numerics.Vector4>(outputBytes.AsSpan(Marshal.SizeOf<ChunkParamsStruct>())).CopyTo(vec4_array);
        
        // Print values for debugging
        GD.Print("DEBUG: reading back params buffer...");
        GD.Print($"CSP: {outputParams.CSP}");
        GD.Print($"CSP3: {outputParams.CSP3}");
        GD.Print($"GenerateCaves: {outputParams.GenerateCaves}");
        GD.Print($"ForceFloor: {outputParams.ForceFloor}");
        GD.Print($"MaxWorldHeight: {outputParams.MaxWorldHeight}");
        GD.Print($"StoneBlockID: {outputParams.StoneBlockID}");
        GD.Print($"OceanHeight: {outputParams.OceanHeight}");
        GD.Print($"NoiseScale: {outputParams.NoiseScale}");
        GD.Print($"CaveNoiseScale: {outputParams.CaveNoiseScale}");
        GD.Print($"CaveThreshold: {outputParams.CaveThreshold}");
        GD.Print($"SeedOffset (VEC4): {outputParams.SeedOffset}");
        GD.Print($"NumChunksToCompute: {outputParams.NumChunksToCompute}");
        GD.Print($"VAR VEC4 Array: {string.Join(",", vec4_array)}");
    }

    public static void PrintDebugVariables()
    {
        GD.Print("MemoryMarshal size of ChunkParamsStruct in bytes: " + Marshal.SizeOf<ChunkParamsStruct>());
        GD.Print("MemoryMarshal size of System.Numerics.Vector4 in bytes: " + Marshal.SizeOf<System.Numerics.Vector4>());
        
        GD.Print("MemoryMarshal size of input params boolean in bytes: " + Marshal.SizeOf<bool>());
        GD.Print("sizeof(bool): " + sizeof(bool));

        /*
        int offset = 0;
        GD.Print($"SeedOffset: {offset}"); offset += 16;
        GD.Print($"CSP: {offset}"); offset += 4;
        GD.Print($"CSP3: {offset}"); offset += 4;
        GD.Print($"NumChunksToCompute: {offset}"); offset += 4;
        GD.Print($"MaxWorldHeight: {offset}"); offset += 4;
        GD.Print($"StoneBlockID: {offset}"); offset += 4;
        GD.Print($"OceanHeight: {offset}"); offset += 4;
        GD.Print($"NoiseLayerCount: {offset}"); offset += 4;
        GD.Print($"NoiseSeed: {offset}"); offset += 4;
        GD.Print($"NoiseScale: {offset}"); offset += 4;
        GD.Print($"CaveNoiseScale: {offset}"); offset += 4;
        GD.Print($"CaveThreshold: {offset}"); offset += 4;
        GD.Print($"GenerateCaves: {offset}"); offset += 4;
        GD.Print($"ForceFloor: {offset}"); offset += 4;
        GD.Print($"Padding1: {offset}"); offset += 4;
        GD.Print($"Padding2: {offset}"); offset += 4;
        GD.Print($"Padding3: {offset}"); offset += 4;
        GD.Print($"ChunkPositions START: {offset}");
        */
    }

    public static void GenerateMultiChunksSequentially(List<Vector3I> chunksToGenerate)
    {
        var stopwatch = Stopwatch.StartNew();
        GD.Print("Running compute shader");

        var _rd = RenderingServer.CreateLocalRenderingDevice();
        var _shader_spir_v = _shader_file.GetSpirV();
        var _compute_shader = _rd.ShaderCreateFromSpirV(_shader_spir_v);

        var noiseBufferBytes = GenerateNoiseLayerBufferBytes();
        var _noise_buffer_rid = _rd.StorageBufferCreate((uint)noiseBufferBytes.Length, noiseBufferBytes);
        var _noise_uniform = GenerateUniform(_noise_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 2);

        var _compute_pipeline = _rd.ComputePipelineCreate(_compute_shader);
        var workgroups = (uint)ChunkManager.CSP / 8;
        var rids = new Rid[chunksToGenerate.Count][];
        for (int i=0; i<chunksToGenerate.Count; i++)
        {
            var chunkPosition = chunksToGenerate[i];
            var paramsBufferBytes = GenerateParameterBufferBytes([chunkPosition]);
            var _params_buffer_rid = _rd.StorageBufferCreate((uint)paramsBufferBytes.Length, paramsBufferBytes);
            var _params_uniform = GenerateUniform(_params_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 1);

            var _num_blocks = ChunkManager.CSP3;
            var _voxel_buffer_rid = GenerateIntBuffer(_rd, _num_blocks);
            var _voxel_array_uniform = GenerateUniform(_voxel_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 0);

            var atomicCounterBytes = new byte[sizeof(uint)];
            var _atomic_counter_rid = _rd.StorageBufferCreate((uint)atomicCounterBytes.Length, atomicCounterBytes);
            var _atomic_counter_uniform = GenerateUniform(_atomic_counter_rid, RenderingDevice.UniformType.StorageBuffer, 3);

            var test_buffer = new byte[8*sizeof(int)];
            var test_buffer_rid = _rd.StorageBufferCreate((uint)test_buffer.Length, test_buffer);
            var test_buffer_unifom = GenerateUniform(test_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 4);

            var _bindings = new Godot.Collections.Array<RDUniform> { _voxel_array_uniform, _params_uniform, _noise_uniform, _atomic_counter_uniform, test_buffer_unifom };
            var _uniform_set = _rd.UniformSetCreate(_bindings, _compute_shader, 0);
            
            var _compute_list = _rd.ComputeListBegin();
            _rd.ComputeListBindComputePipeline(_compute_list, _compute_pipeline);
            _rd.ComputeListBindUniformSet(_compute_list, _uniform_set, 0);
            _rd.ComputeListDispatch(_compute_list, workgroups, workgroups, workgroups);
            //_rd.ComputeListAddBarrier(_compute_list);
            _rd.ComputeListEnd();
            rids[i] = [_uniform_set, _voxel_buffer_rid, _params_buffer_rid, _atomic_counter_rid, test_buffer_rid];
        }
        
        _rd.Submit();
        _rd.Sync();

        foreach (Rid[] rid_array in rids)
        {
            var _voxel_buffer_rid = rid_array[1];
            var _params_buffer_rid = rid_array[2];
            var voxel_bytes = _rd.BufferGetData(_voxel_buffer_rid);
            var params_bytes = _rd.BufferGetData(_params_buffer_rid);
            
            var data = new int[ChunkManager.CSP3];
            Buffer.BlockCopy(voxel_bytes, 0, data, 0, voxel_bytes.Length);

            var outputParams = MemoryMarshal.Read<ChunkParamsStruct>(params_bytes);
            // Extract the float array (from structSize to end)
            var vec4_array = new System.Numerics.Vector4[outputParams.NumChunksToCompute];
            MemoryMarshal.Cast<byte, System.Numerics.Vector4>(params_bytes.AsSpan(Marshal.SizeOf<ChunkParamsStruct>())).CopyTo(vec4_array);
            var chunkPosition = new Vector3I((int)vec4_array[0].X,(int)vec4_array[0].Y,(int)vec4_array[0].Z);

            //GD.Print("array length: " + array.Length);
            //GD.Print("array length/sizeof(int): " + array.Length/sizeof(int));
            //GD.Print("data length: " + data.Length);
            if (data.All(x => x == 0)) GD.Print("data is all zeroes");

            ChunkManager.BLOCKCACHE[chunkPosition] = data;
        }

        /*
        GD.Print($"Generated chunk at: " + chunkPosition);
        var tempstring = "[";
        for (int i = 0; i < 20; i++)
        {
            tempstring += ChunkManager.BLOCKCACHE[chunkPosition][i] + ",";
        }
        tempstring += "...]";
        GD.Print(tempstring);
        */

        FreeRids(_rd, rids);
        
        _rd.FreeRid(_noise_buffer_rid);
        if (_compute_pipeline.IsValid) _rd.FreeRid(_compute_pipeline);
        _rd.FreeRid(_compute_shader);

        stopwatch.Stop();
        GD.Print($"GenerateMultiChunksSequentially time elapsed: {stopwatch.ElapsedMilliseconds} ms");
    }

    public static void GenerateBlocks(Vector3I chunkPosition)
    {
        var _rd = RenderingServer.CreateLocalRenderingDevice();
        var _shader_spir_v = _shader_file.GetSpirV();
        var _compute_shader = _rd.ShaderCreateFromSpirV(_shader_spir_v);

        var _num_blocks = ChunkManager.CSP3;
        var _voxel_buffer_rid = GenerateIntBuffer(_rd, _num_blocks);
        var _voxel_array_uniform = GenerateUniform(_voxel_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 0);

        var paramsBufferBytes = GenerateParameterBufferBytes([chunkPosition]);
        var _params_buffer_rid = _rd.StorageBufferCreate((uint)paramsBufferBytes.Length, paramsBufferBytes);
        var _params_uniform = GenerateUniform(_params_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 1);

        var noiseBufferBytes = GenerateNoiseLayerBufferBytes();
        var _noise_buffer_rid = _rd.StorageBufferCreate((uint)noiseBufferBytes.Length, noiseBufferBytes);
        var _noise_uniform = GenerateUniform(_noise_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 2);

        var atomicCounterBytes = new byte[sizeof(uint)];
        var _atomic_counter_rid = _rd.StorageBufferCreate((uint)atomicCounterBytes.Length, atomicCounterBytes);
        var _atomic_counter_uniform = GenerateUniform(_atomic_counter_rid, RenderingDevice.UniformType.StorageBuffer, 3);

        var test_buffer = new byte[8*sizeof(int)];
        var test_buffer_rid = _rd.StorageBufferCreate((uint)test_buffer.Length, test_buffer);
        var test_buffer_unifom = GenerateUniform(test_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 4);

        var _bindings = new Godot.Collections.Array<RDUniform> { _voxel_array_uniform, _params_uniform, _noise_uniform, _atomic_counter_uniform, test_buffer_unifom };

        var _uniform_set = _rd.UniformSetCreate(_bindings, _compute_shader, 0);
        
        var workgroups = (uint)ChunkManager.CSP / 8;
        var _compute_pipeline = _rd.ComputePipelineCreate(_compute_shader);
        var _compute_list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(_compute_list, _compute_pipeline);
        _rd.ComputeListBindUniformSet(_compute_list, _uniform_set, 0);
        _rd.ComputeListDispatch(_compute_list, workgroups, workgroups, workgroups);
        //_rd.ComputeListAddBarrier(_compute_list);
        _rd.ComputeListEnd();
        _rd.Submit();

        var rids = new Rid[] {_uniform_set, _voxel_buffer_rid, _params_buffer_rid, _noise_buffer_rid, _atomic_counter_rid, _compute_pipeline, _compute_shader};

        _rd.Sync();
        var array = _rd.BufferGetData(_voxel_buffer_rid);
        
        var data = new int[ChunkManager.CSP3];
        Buffer.BlockCopy(array, 0, data, 0, array.Length);

        //GD.Print("array length: " + array.Length);
        //GD.Print("array length/sizeof(int): " + array.Length/sizeof(int));
        //GD.Print("data length: " + data.Length);
        if (data.All(x => x == 0)) GD.Print("data is all zeroes");

        ChunkManager.BLOCKCACHE[chunkPosition] = data;
        /*
        GD.Print($"Generated chunk at: " + chunkPosition);
        var tempstring = "[";
        for (int i = 0; i < 20; i++)
        {
            tempstring += ChunkManager.BLOCKCACHE[chunkPosition][i] + ",";
        }
        tempstring += "...]";
        GD.Print(tempstring);
        */

        FreeRids(_rd, rids);
        /*

        var lambda = new Action<byte[]> ((array) =>
            {
                GD.Print("Generated chunk at: " + chunkPosition);
                var data = new int[ChunkManager.CSP3];
                Buffer.BlockCopy(array, 0, data, 0, array.Length);

                ChunkManager.BLOCKCACHE[chunkPosition] = data;
                var tempstring = "";
                for (int i = 0; i < 20; i++)
                {
                    tempstring += ChunkManager.BLOCKCACHE[chunkPosition][i] + " ";
                }
                tempstring += "...";
                GD.Print(tempstring);

                RenderingServer.CallOnRenderThread(Callable.From(() =>
                {
                    foreach (Rid r in rids)
                    {
                        _rd.FreeRid(r);
                    }
                    _rd.Free();
                }));
            }
        );

        var lambdatest = new Action<string>(GD.Print);

        var calltest = Callable.From(lambdatest);

        var callable = Callable.From(lambda);
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            var err = _rd.BufferGetDataAsync(_voxel_buffer_rid, callable, 0, ChunkManager.CSP3*sizeof(int));
            GD.Print(err);
        }));

        calltest.CallDeferred("this is a test");*/
    }

    private static void FreeRids(RenderingDevice _rd, Rid[] rids)
    {
        foreach (Rid r in rids)
        {
            if(r.IsValid) _rd.FreeRid(r); 
        }
        //_rd.Free();
    }
    private static void FreeRids(RenderingDevice _rd, Rid[][] rids)
    {
        foreach (Rid[] rid_array in rids)
        {
            foreach (Rid r in rid_array) if(r.IsValid) _rd.FreeRid(r);
        }
        //_rd.Free();
    }

    public static void FreeRenderingDevice(RenderingDevice _rd = null)
    {
        if (_rd == null) return;
        _rd.Sync();
        _rd.Free();
    }

    private static Rid GenerateIntBuffer(RenderingDevice _rd, int size)
    {
        var dataBufferBytes = new byte[size * sizeof(int)];
        return _rd.StorageBufferCreate((uint)dataBufferBytes.Length, dataBufferBytes);
    }

    private static RDUniform GenerateUniform(Rid dataBuffer, RenderingDevice.UniformType type, int binding)
    {
        var dataUniform = new RDUniform
        {
            UniformType = type,
            Binding = binding
        };
        dataUniform.AddId(dataBuffer);
        return dataUniform;
    }

    private static byte[] GenerateNoiseLayerBufferBytes()
    {
        var struct_size = Marshal.SizeOf<NoiseLayerStruct>();
        var data_bytes = new byte[_noise_layers.Length * struct_size]; // Allocate space for struct + vec3 array

        // Copy the struct data into the start of the byte array
        for (int i=0; i< _noise_layers.Length; i++)
        {
            MemoryMarshal.AsBytes(new Span<NoiseLayerStruct>(ref _noise_layers[i])).CopyTo(data_bytes.AsSpan()[(i*struct_size)..]);
        }

        return data_bytes;
    }

    private static byte[] GenerateParameterBufferBytes(List<Vector3I> chunkPositions)
    {
        Vector3 seedOffset = new(0, 0, 0);
        var chunk_positions = new System.Numerics.Vector4[chunkPositions.Count];
        for (var i=0; i<chunkPositions.Count; i++)
        {
            chunk_positions[i] = new System.Numerics.Vector4(chunkPositions[i].X, chunkPositions[i].Y, chunkPositions[i].Z,0.0f);
        }

        input_params.SeedOffset = new System.Numerics.Vector4(seedOffset.X, seedOffset.Y, seedOffset.Z, 0.0f);
        input_params.NumChunksToCompute = chunkPositions.Count;
        input_params.MaxWorldHeight = MaxWorldHeight;
        input_params.CaveNoiseScale = CaveNoiseScale;
        input_params.CaveThreshold = CaveThreshold;
        input_params.OceanHeight = OceanHeight;
        input_params.GenerateCaves = GenerateCaves;
        input_params.ForceFloor = ForceFloor;

        // Get the struct size
        // note that MemoryMarshal interprets bools as 4 bytes for interoperability,
        // this makes it preferable to the sizeof(bool) operator which = 1 byte
        // and makes padding with the gpu harder (glsl pads bools to 4 bytes)
        var struct_size = Marshal.SizeOf<ChunkParamsStruct>();
        
        var data_bytes = new byte[struct_size + (chunkPositions.Count * Marshal.SizeOf<System.Numerics.Vector4>())]; // Allocate space for struct + vec3 array

        // Copy the struct data into the start of the byte array
        
        MemoryMarshal.AsBytes(new Span<ChunkParamsStruct>(ref input_params)).CopyTo(data_bytes.AsSpan());

        // Serialize the vec3 (padded to vec4) array and append it to the byte array
        var vec4_array_bytes = MemoryMarshal.AsBytes<System.Numerics.Vector4>(chunk_positions).ToArray();
        vec4_array_bytes.CopyTo(data_bytes.AsSpan(struct_size));

        //GD.Print($"Generated parameter buffer bytes {data_bytes.Length}");

        return data_bytes;
    }
}
