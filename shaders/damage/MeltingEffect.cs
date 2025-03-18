using Godot;
using System;
using System.Collections.Generic;

public partial class MeltingEffect : ColorRect
{
    private bool melting = false;
    private float timer = 0.0f;
    
    // XResolution should match the shader const "num_bins"
    [Export] public float XResolution { get; set; } = 100.0f;
    [Export] public float MaxOffset { get; set; } = 2.0f;
    [Export] public float Duration { get; set; } = 0.5f;

    public override void _Ready()
    {
        Hide();
    }

    public void Reset()
    {
        timer = 0.0f;
        ((ShaderMaterial)Material).SetShaderParameter("t", 0.0f);
        Hide();
    }

    public override void _Process(double delta)
    {
        if (melting)
        {
            timer += (float)delta;
            ((ShaderMaterial)Material).SetShaderParameter("t", timer / Duration);
        }
        
        if (timer >= Duration)
        {
            melting = false;
            Reset();
        }
    }

    public void GenerateOffsets()
    {
        var offsets = new List<float>();
        
        for (int i = 0; i < XResolution; i++)
        {
            offsets.Add((float)(Random.Shared.NextDouble() * (MaxOffset - 1.0) + 1.0));
        }
        
        ((ShaderMaterial)Material).SetShaderParameter("y_offsets", offsets.ToArray());
        
        var img = GetViewport().GetTexture().GetImage();
        var tex = ImageTexture.CreateFromImage(img);

        ((ShaderMaterial)Material).SetShaderParameter("melt_tex", tex);
        
        Show();
    }

    public void Transition()
    {
        ((ShaderMaterial)Material).SetShaderParameter("melting", true);
        melting = true;
    }

    public void Melt()
    {
        Reset();
        GenerateOffsets();
        Transition();
    }
}