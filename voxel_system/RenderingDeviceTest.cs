using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

public partial class RenderingDeviceTest : Node
{
    private static RDShaderFile _shaderFile = ResourceLoader.Load<RDShaderFile>("res://voxel_system/test.glsl");
    public static RenderingDeviceTest Instance {get; private set;}

    public override void _Ready()
    {
        Instance = this;
    }

    public static void RunAsyncCallbackTest()
    {
        Instance.Test();
    }

    public void Test()
    {
        var _rd = RenderingServer.CreateLocalRenderingDevice();
        var shader_rid = _rd.ShaderCreateFromSpirV(_shaderFile.GetSpirV());

        var data = new float[10000];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = i;
        }
        var data_bytes = MemoryMarshal.AsBytes(data.AsSpan()).ToArray();
        var data_buffer_rid = _rd.StorageBufferCreate((uint)data_bytes.Length, data_bytes);
        var _buffer_uniform = GenerateUniform(data_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 0);

        var placeholder = new uint[] { 0 };
        var plaeholder_bytes = MemoryMarshal.AsBytes(placeholder.AsSpan()).ToArray();
        var placeholder_rid = _rd.StorageBufferCreate((uint)plaeholder_bytes.Length, plaeholder_bytes);
        var placeholder_uniform = GenerateUniform(placeholder_rid, RenderingDevice.UniformType.StorageBuffer, 1);

        var bindings = new Godot.Collections.Array<RDUniform> {_buffer_uniform, placeholder_uniform };

        var uniform_set = _rd.UniformSetCreate(bindings, shader_rid, 0);
        
        var compute_pipeline = _rd.ComputePipelineCreate(shader_rid);
        var compute_list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(compute_list, compute_pipeline);
        _rd.ComputeListBindUniformSet(compute_list, uniform_set, 0);
        _rd.ComputeListDispatch(compute_list, 100, 1, 1);
        _rd.ComputeListEnd();
        _rd.Submit();

        var _rids_to_free = new List<Rid>();
        _rids_to_free.AddRange([compute_pipeline, uniform_set, data_buffer_rid, placeholder_rid, shader_rid]);

        var data_fetch_timer = new Stopwatch();

        var lambda = (Variant data_var) => 
        {
            var data = data_var.AsByteArray();
            //data_fetch_timer.Stop();
            //GD.Print($"Data fetch took {data_fetch_timer.ElapsedMilliseconds}ms");
            GD.Print("called callback");
            var data_float = MemoryMarshal.Cast<byte, float>(data).ToArray();
            GD.Print(data_float.Take(10).ToArray().Join(","));
            foreach (var rid in _rids_to_free)
            {
                _rd.FreeRid(rid);
            }
            _rd.Free();
        };
        var callable = Callable.From(lambda);

        var sync_timer = new Stopwatch();
        sync_timer.Start();
        _rd.Sync();
        sync_timer.Stop();
        GD.Print($"Sync took {sync_timer.ElapsedMilliseconds}ms");

        GD.Print("Fetching buffer data...");

        //RenderingServer.CallOnRenderThread(Callable.From(() => {
            data_fetch_timer.Start();
            _rd.BufferGetDataAsync(data_buffer_rid, callable);
            //_rd.BufferGetData(placeholder_rid);
       // }));

        // RenderingServer.CallOnRenderThread(Callable.From(() => {
        //     data_fetch_timer.Start();
        //     var data = _rd.BufferGetData(placeholder_rid);
        //     callable.Call(data);
        // }));
    }

        // var t = new Timer()
        // {
        //     WaitTime = 0.016f,
        //     OneShot = true
        // };
        // t.Timeout += () => {
        //     var sync_timer = new Stopwatch();
        //     _rd.BufferGetDataAsync(data_buffer_rid, Callable.From(lambda));
        //     sync_timer.Start();
        //     _rd.Sync();
        //     sync_timer.Stop();
        //     GD.Print($"Sync took {sync_timer.ElapsedMilliseconds}ms");
        //     t.QueueFree();
        // };
        // Instance.AddSibling(t);
        // t.Start();

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
}
