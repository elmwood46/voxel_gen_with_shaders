using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public partial class ComputeManager : Node
{
    public static readonly RenderingDevice LocalRenderingDevice = RenderingServer.CreateLocalRenderingDevice();
    private static readonly RDShaderFile _vox_shader_file = ResourceLoader.Load<RDShaderFile>("res://compute_shaders/version2/chunkgen.glsl");
    private static readonly RDShaderFile _mesh_shader_file = ResourceLoader.Load<RDShaderFile>("res://compute_shaders/version2/compute_meshing.glsl");
    private static Rid _voxel_gen_shader;
    private static Rid _mesh_gen_shader;

    // private static readonly List<MeshBuffer> _allMeshComputeBuffers = [];
    // private static readonly Queue<MeshBuffer> _availableMeshComputeBuffers = new();
    // private static readonly List<VoxelBuffer> _allVoxelComputeBuffers = [];
    // private static readonly Queue<VoxelBuffer> _availableVoxelComputeBuffers = new();

    private uint _workgroup_size = ChunkManager.CSP / 8;
    // private uint _meshing_workgroup_size = ChunkManager.CHUNK_SIZE / 6;
    // public int _numberMeshBuffers = 0;

    // private static Rid _noiseLayerParamsBuffer;
    // private byte[] _noiseLayerParamsBufferData;
    // private static Rid _chunkParamsBuffer;
    // private byte[] _chunkParamsBufferData;

    [ExportCategory("Chunk Gen Settings")]
    // [Export(PropertyHint.Range, "1,32,")] public int NumComputeBuffers = 18;
    [Export] public ChunkGenParamsResource ChunkGenParams {get;set;} = new ChunkGenParamsResource();

    [ExportCategory("Noise Settings")]
    public int seed;
    [Export] public Godot.Collections.Array<NoiseLayerResource> NoiseLayers {get;set;} = [new NoiseLayerResource()];

    public static ComputeManager Instance {get; private set;}

    public override void _Ready()
    {
        Instance = this;
        // _voxel_gen_shader = LocalRenderingDevice.ShaderCreateFromSpirV(_vox_shader_file.GetSpirV());
        // _mesh_gen_shader = LocalRenderingDevice.ShaderCreateFromSpirV(_mesh_shader_file.GetSpirV());
        
        // Initialize(NumComputeBuffers);
    }

    public void Initialize(int count = 18)
    {
        
        // // add noise layer data to buffer
        // var noise_layer_struct_size = Marshal.SizeOf<NoiseLayerStruct>();
        // _noiseLayerParamsBufferData = new byte[noise_layer_struct_size*NoiseLayers.Count];
        // var noise_layer_array = new NoiseLayerStruct[NoiseLayers.Count];
        // for (var i=0; i < noise_layer_array.Length; i++) noise_layer_array[i] = new NoiseLayerStruct(NoiseLayers[i]);
        // var noise_layer_array_bytes = MemoryMarshal.AsBytes<NoiseLayerStruct>(noise_layer_array).ToArray();
        // noise_layer_array_bytes.CopyTo(_noiseLayerParamsBufferData.AsSpan());
        // _noiseLayerParamsBuffer = LocalRenderingDevice.StorageBufferCreate((uint)_noiseLayerParamsBufferData.Length, _noiseLayerParamsBufferData);

        // _chunkParamsBufferData = new byte[Marshal.SizeOf<ChunkParamsStruct>()];
        // var chunk_params_bytes = MemoryMarshal.AsBytes([new ChunkParamsStruct(ChunkGenParams)]);
        // chunk_params_bytes.CopyTo(_chunkParamsBufferData);
        // _chunkParamsBuffer = LocalRenderingDevice.StorageBufferCreate((uint)_chunkParamsBufferData.Length, _chunkParamsBufferData);

        for (int i = 0; i < count; i++)
        {
            //CreateNewComputeBuffer("voxel", true);
            //CreateNewComputeBuffer("mesh", true);
        }
    }
    
    public void GenerateVoxelData(Chunk chunk, Vector3I chunk_position)
    {
        RenderingServer.CallOnRenderThread(Callable.From(() => {
            var _rd = RenderingServer.CreateLocalRenderingDevice();

            var _voxel_gen_shader = _rd.ShaderCreateFromSpirV(_vox_shader_file.GetSpirV());

            // add noise layer data to buffer
            var noise_layer_struct_size = Marshal.SizeOf<NoiseLayerStruct>();
            var _noiseLayerParamsBufferData = new byte[noise_layer_struct_size*NoiseLayers.Count];
            var noise_layer_array = new NoiseLayerStruct[NoiseLayers.Count];
            for (var i=0; i < noise_layer_array.Length; i++) noise_layer_array[i] = new NoiseLayerStruct(NoiseLayers[i]);
            var noise_layer_array_bytes = MemoryMarshal.AsBytes<NoiseLayerStruct>(noise_layer_array).ToArray();
            noise_layer_array_bytes.CopyTo(_noiseLayerParamsBufferData.AsSpan());
            var _noiseLayerParamsBuffer = _rd.StorageBufferCreate((uint)_noiseLayerParamsBufferData.Length, _noiseLayerParamsBufferData);

            var _chunkParamsBufferData = new byte[Marshal.SizeOf<ChunkParamsStruct>()];
            var chunk_params_bytes = MemoryMarshal.AsBytes([new ChunkParamsStruct(ChunkGenParams)]);
            chunk_params_bytes.CopyTo(_chunkParamsBufferData);
            var _chunkParamsBuffer = _rd.StorageBufferCreate((uint)_chunkParamsBufferData.Length, _chunkParamsBufferData);
            
            var vox_buffer = new byte[sizeof(int)*ChunkManager.CSP3];
            var vox_buffer_rid = _rd.StorageBufferCreate((uint)vox_buffer.Length, vox_buffer);
            var vox_uniform = BufferToUniform(vox_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 0);

            var _chunk_pos_bytes = MemoryMarshal.AsBytes([chunk_position]).ToArray();
            var _chunk_pos_buffer_rid = _rd.StorageBufferCreate((uint)_chunk_pos_bytes.Length, _chunk_pos_bytes);
            var _chunk_pos_uniform = BufferToUniform(_chunk_pos_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 1);

            var params_uniform = BufferToUniform(_chunkParamsBuffer, RenderingDevice.UniformType.StorageBuffer, 2);
            var noise_uniform = BufferToUniform(_noiseLayerParamsBuffer, RenderingDevice.UniformType.StorageBuffer, 3);

            var _bindings = new Godot.Collections.Array<RDUniform> { vox_uniform, _chunk_pos_uniform, params_uniform, noise_uniform};

            var _uniform_set = _rd.UniformSetCreate(_bindings, _voxel_gen_shader, 0);

            var _compute_pipeline = _rd.ComputePipelineCreate(_voxel_gen_shader);
            var _compute_list = _rd.ComputeListBegin();
            _rd.ComputeListBindComputePipeline(_compute_list, _compute_pipeline);
            _rd.ComputeListBindUniformSet(_compute_list, _uniform_set, 0);
            _rd.ComputeListDispatch(_compute_list, _workgroup_size, _workgroup_size, _workgroup_size);
            _rd.ComputeListEnd();
    
            _rd.Submit();

            var rids_to_free = new List<Rid> {vox_buffer_rid, _noiseLayerParamsBuffer, _chunkParamsBuffer,
            _chunk_pos_buffer_rid, _uniform_set, _compute_pipeline, _voxel_gen_shader};

            var lambda = (byte[] voxels) =>
            {
                if (!ChunkManager.BLOCKCACHE.TryGetValue(chunk_position, out var cachedData))
                    ChunkManager.BLOCKCACHE[chunk_position] = new int[ChunkManager.CSP3];
                
                MemoryMarshal.Cast<byte, int>(voxels.AsSpan()).CopyTo(ChunkManager.BLOCKCACHE[chunk_position]);

                var mesh = ChunkManager.BuildChunkMesh(chunk_position);
                chunk.MeshInstance.Mesh = mesh;
                foreach (Rid r in rids_to_free) if (r.IsValid) _rd.FreeRid(r);
                _rd.Free();
            };

            var t = new Timer()
            {
                WaitTime = 0.16f,
                OneShot = true
            };
            t.Timeout += () => {

                _rd.BufferGetDataAsync(vox_buffer_rid, Callable.From(lambda));
                _rd.Sync();
                t.QueueFree();
            };
            AddSibling(t);
        }));
    }

    private static RDUniform BufferToUniform(Rid dataBuffer, RenderingDevice.UniformType type, int binding)
    {
        var dataUniform = new RDUniform
        {
            UniformType = type,
            Binding = binding
        };
        dataUniform.AddId(dataBuffer);
        return dataUniform;
    }

    // public static IComputeBuffer GetBuffer(string type)
    // {
    //     if (!(type == "mesh" || type == "voxel")) throw new Exception("Invalid buffer type");

    //     if (type == "mesh")
    //     {
    //         if (_availableMeshComputeBuffers.Count > 0) return _availableMeshComputeBuffers.Dequeue();
    //         return CreateNewComputeBuffer(type, false);
    //     }
    //     else if (type == "voxel")
    //     {
    //         if (_availableVoxelComputeBuffers.Count > 0) return _availableVoxelComputeBuffers.Dequeue(); 
    //         return CreateNewComputeBuffer(type, false);
    //     }

    //     throw new Exception("Unhandled buffer type");
    // }

    // public static IComputeBuffer CreateNewComputeBuffer(string type, bool enqueue)
    // {
    //     if (!(type == "mesh" || type == "voxel")) throw new Exception("Invalid buffer type");

    //     IComputeBuffer buffer;
        
    //     buffer = type == "mesh" ? new MeshBuffer() : new VoxelBuffer();
    //     buffer.InitializeBuffer();

    //     if (type == "mesh")
    //     {
    //         _allMeshComputeBuffers.Add((MeshBuffer)buffer);
    //         if (enqueue) _availableMeshComputeBuffers.Enqueue((MeshBuffer)buffer);
    //     }
    //     else if (type == "voxel")
    //     {
    //         _allVoxelComputeBuffers.Add((VoxelBuffer)buffer);
    //         if (enqueue) _availableVoxelComputeBuffers.Enqueue((VoxelBuffer)buffer);
    //     }

    //     return buffer;
    // }

    // public static void FreeAllRids()
    // {
    //     foreach (var buffer in _allMeshComputeBuffers) buffer.Deactivate_And_FreeBufferRids();
    //     foreach (var buffer in _allVoxelComputeBuffers) buffer.Deactivate_And_FreeBufferRids();
    //     LocalRenderingDevice.FreeRid(_noiseLayerParamsBuffer);
    //     LocalRenderingDevice.FreeRid(_chunkParamsBuffer);
    //     LocalRenderingDevice.FreeRid(_voxel_gen_shader);
    //     LocalRenderingDevice.FreeRid(_mesh_gen_shader);
    //     LocalRenderingDevice.Free();
    // }

    // ---------------------------------------------------
    // Structs
    // ---------------------------------------------------

    public interface IComputeBuffer
    {
        public void InitializeBuffer();
        public void Deactivate_And_FreeBufferRids();
        public void ClearAllBufferData();
    }

    public struct VoxelBuffer : IComputeBuffer
    {
        public Rid VoxelBufferRid {get;private set;}
        public byte[] VoxelBufferData {get;private set;}
        public bool Initialized;

        public void InitializeBuffer()
        {
            if (Initialized)
                return;
            
            if (!IsInstanceValid(LocalRenderingDevice))
                return;

            VoxelBufferData = new byte[ChunkManager.CSP3*sizeof(int)];
            VoxelBufferRid = LocalRenderingDevice.StorageBufferCreate((uint)VoxelBufferData.Length, VoxelBufferData);

            Initialized = true;
        }

        public readonly void ClearAllBufferData()
        {
            if (!IsInstanceValid(LocalRenderingDevice))
                return;

            Array.Clear(VoxelBufferData);
        }

        public void Deactivate_And_FreeBufferRids()
        {
            if (!IsInstanceValid(LocalRenderingDevice))
                return;

            LocalRenderingDevice.FreeRid(VoxelBufferRid);

            Initialized = false;
        }
    }

    public struct MeshBuffer : IComputeBuffer
    {
        const int MAX_VERTS_PER_CHUNK = 6*6*ChunkManager.CHUNK_SIZE*ChunkManager.CHUNK_SIZE*ChunkManager.CHUNK_SIZE/2;
        public static readonly int VEC3_SIZE = Marshal.SizeOf<Vector3>();
        public static readonly int VEC2_SIZE = Marshal.SizeOf<Vector2>();
        public Rid VertexBufferRid {get;private set;}
        public byte[] VertexBufferData {get;private set;}
        public Rid NormalBufferRid {get;private set;}
        public byte[] NormalBufferData {get;private set;}
        public Rid UVBufferRid {get;private set;}
        public byte[] UVBufferData {get;private set;}
        public Rid AtomicCounterBufferRid {get;private set;}
        public byte[] AtomicCounterBufferData {get;private set;}
        public bool Initialized;

        public void InitializeBuffer()
        {
            if (Initialized)
                return;
            
            if (!IsInstanceValid(LocalRenderingDevice))
                return;

            AtomicCounterBufferData = new byte[sizeof(int)];
            AtomicCounterBufferRid = LocalRenderingDevice.StorageBufferCreate((uint)AtomicCounterBufferData.Length, AtomicCounterBufferData);

            VertexBufferData = new byte[MAX_VERTS_PER_CHUNK*VEC3_SIZE];
            VertexBufferRid = LocalRenderingDevice.StorageBufferCreate((uint)VertexBufferData.Length, VertexBufferData);

            NormalBufferData = new byte[MAX_VERTS_PER_CHUNK*VEC3_SIZE];
            NormalBufferRid = LocalRenderingDevice.StorageBufferCreate((uint)NormalBufferData.Length, NormalBufferData);

            UVBufferData = new byte[MAX_VERTS_PER_CHUNK*VEC2_SIZE];
            UVBufferRid = LocalRenderingDevice.StorageBufferCreate((uint)UVBufferData.Length, UVBufferData);

            Initialized = true;
        }

        public readonly void ClearAllBufferData()
        {
            if (!IsInstanceValid(LocalRenderingDevice))
                return;

            Array.Clear(VertexBufferData);
            Array.Clear(NormalBufferData);
            Array.Clear(UVBufferData);
            Array.Clear(AtomicCounterBufferData);
        }

        public void Deactivate_And_FreeBufferRids()
        {
            if (!IsInstanceValid(LocalRenderingDevice))
                return;

            LocalRenderingDevice.FreeRid(UVBufferRid);
            LocalRenderingDevice.FreeRid(NormalBufferRid);
            LocalRenderingDevice.FreeRid(VertexBufferRid);
            LocalRenderingDevice.FreeRid(AtomicCounterBufferRid);

            Initialized = false;
        }
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

        public NoiseLayerStruct(NoiseLayerResource noiseLayer)
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

        public ChunkParamsStruct(ChunkGenParamsResource c)
        {
            SeedOffset = new System.Numerics.Vector4(c.SeedOffset.X, c.SeedOffset.Y, c.SeedOffset.Z, 0.0f);
            CSP = c.CSP;
            CSP3 = c.CSP3;
            NumChunksToCompute = c.NumChunksToCompute;
            MaxWorldHeight = c.MaxWorldHeight;
            StoneBlockID = c.StoneBlockID;
            OceanHeight = c.OceanHeight;
            NoiseLayerCount = c.NoiseLayerCount;
            NoiseSeed = c.NoiseSeed;
            NoiseScale = c.NoiseScale;
            CaveNoiseScale = c.CaveNoiseScale;
            CaveThreshold = c.CaveThreshold;
            GenerateCaves = c.GenerateCaves;
            ForceFloor = c.ForceFloor;
        }
    }
}

