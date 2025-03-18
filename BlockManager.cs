using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Linq;

// note each block has 6 textures from 0-5
// order: bottom, top, left, right, back, front 
// NOTE that the "back" texture faces in the -z direction (which is "forward" in Godot's physics)
// the "front" texture faces in the +z direction (which is "back" in Godot's physics)
// this is because we construct the blocks in "chunk space" and don't transform the textures to godot's physics space

// this is the type of block
// a block's group determines its resilience to damage and other things like footstep sounds
public enum BlockSpecies {
	Air,
	Lava,
	Stone,
	Porcelain,
	Dirt,
	Grass,
	Gravel,
	Wood,
	Leaves,
	Brick,
	GoldOre
}

// used to shade blocks when they get damaged
public enum DamageType {
    Physical, // 6th bit in block info integer
    Fire,    // 7th bit in block info integer
    Acid     // 8th bit
}

[Tool]
public partial class BlockManager : Node
{
	// blocks have 5 bits to store their damage level and break when it's >= 31, maxed out (11111 = 31)
	public const int BLOCK_BREAK_DAMAGE_THRESHOLD = 31;

	// note the code assumes that block 0 is the empty (air) block
	// but this has to be set manually
	[Export] public Array<Block> Blocks { get; set; }

	private readonly System.Collections.Generic.Dictionary<Texture2D, int> _texarraylookup = new();

	private readonly System.Collections.Generic.Dictionary<string, int> _blockIdLookup = new();

	public static BlockManager Instance { get; private set; }

	public ShaderMaterial ChunkMaterial { get; private set; }
	public ShaderMaterial ChunkMaterialDamagePulse { get; private set; }

	public ShaderMaterial BrokenBlockShader { get; private set; }
	public ShaderMaterial CellFractureBlockShader { get; private set; }
	public ShaderMaterial DestructibleObjectShader { get; private set; }

	public ShaderMaterial LavaShader { get; private set; }

	public Texture2DArray TextureArray = new();

	public static int LavaBlockId {get; private set;}
	public static int StoneBlockId {get; private set;}
	public static int GrassBlockId {get; private set;}
	public static int DirtBlockId {get; private set;}

	// shader defaults
	private static readonly NoiseTexture2D noise = GD.Load("res://shaders/flamenoise.tres") as NoiseTexture2D;
	private static readonly NoiseTexture2D spotnoise = GD.Load("res://shaders/flame_spotnoise.tres") as NoiseTexture2D;
	private static readonly Color fire_border_colour = new(1.0f,1.0f,1.0f,1.0f);
	private static readonly Color fire_emission_colour = new(0.96f,0.35f,0.0f,1.0f);
	private static readonly Color pulse_colour = new(0.96f,0.88f,0.0f,0.43f);
	private static readonly Color burned_colour = new(0.2f, 0.09f, 0.03f,1.0f);
	private static readonly Color acid_colour = new(0, 0.9490196078f, 0);
	private static readonly Color acid_edge = new(0.0509803922f, 0.6980392157f, 0.0470588235f);
	public static BlockSpecies BlockSpecies(string blockName) {
		return Instance.Blocks[BlockID(blockName)].Species;
	}

	public static int BlockID(string blockName) {
		return Instance._blockIdLookup[blockName];
	}

	public static string BlockName(int blockID) {
		return Instance.Blocks[blockID].Name;
	}

	public static int[] BlockTextureArrayPositions(int blockID) {
		return Instance.Blocks[blockID].BakedTextureArrayPositions;
	}

	private int[] GetBlockTextureArrayPositions(int blockID) {
		var texarray = Instance.Blocks[blockID].Textures;
		var result = new int[texarray.Length];
		for (int i=0; i<texarray.Length; i++) {
			result[i] = _texarraylookup[texarray[i]];
			//GD.Print($"Block {Blocks[blockID].Name} texture {i} is at array position {result[i]}");
		}
		return result;
	}

	public override void _Ready()
	{
		Instance = this;

		LavaShader = GD.Load("res://shaders/LavaShader.tres") as ShaderMaterial;

		if (Blocks[0].Name != "Air") throw new Exception("Blockmanager blocks array was set up incorrectly: block 0 must be the air block.");	
		var enumerable = Blocks.Select(block => {block.SetTextures(); return block;}).SelectMany(block => block.Textures).Where(texture => texture != null).Distinct();
		var blockTextures = enumerable.ToArray();
        var blockImages = new Array<Image> (enumerable.Select(texture =>
				{
					var image = texture.GetImage(); // Create an Image object from the texture
					return image;
				})
			);

		//var tex_array = new Texture2DArray();
		for (int i = 0; i < blockTextures.Length; i++)
		{
			var texture = blockTextures[i];
			_texarraylookup.Add(texture, i);    // map texture to position in texture array
		}
		TextureArray.CreateFromImages(blockImages);

		// get the ordered texture array positions for each block's texture
		// and store them in the block
		for (int i=0;i<Blocks.Count;i++) {
			_blockIdLookup.Add(Blocks[i].Name, i);
			if (Blocks[i].Name == "Lava") LavaBlockId = i;
			if (Blocks[i].Name == "Stone") StoneBlockId = i;
			if (Blocks[i].Name == "Grass") GrassBlockId = i;
			if (Blocks[i].Name == "Dirt") DirtBlockId = i;
			Instance.Blocks[i].BakedTextureArrayPositions = GetBlockTextureArrayPositions(i);
		}

		// setup shader defaults
		ChunkMaterial = ResourceLoader.Load("res://shaders/chunk_uv_shader.tres") as ShaderMaterial;
		ChunkMaterial.SetShaderParameter("_albedo", TextureArray);
		ChunkMaterial.SetShaderParameter("_displacement", GD.Load("res://BlockTextures/textureExperiment/Ground_Dirt_006_DISPa.png"));
		ChunkMaterial.SetShaderParameter("_roughness", GD.Load("res://BlockTextures/textureExperiment/Ground_Dirt_006_ROUGH.jpg"));
		ChunkMaterial.SetShaderParameter("_normalmap", GD.Load("res://BlockTextures/textureExperiment/Ground_Dirt_006_NORM.jpg"));
		ChunkMaterial.SetShaderParameter("_acidcurvetex", GD.Load("res://shaders/acid_damage_curve.tres"));
		ChunkMaterial.SetShaderParameter("_firecurvetex", GD.Load("res://shaders/fire_damage_curve.tres"));
		ChunkMaterial.SetShaderParameter("_damage_pulse_curvetex", GD.Load("res://shaders/pulse_damage_curve.tres"));
		ChunkMaterial.SetShaderParameter("_cracks_texture", GD.Load("res://BlockTextures/textureExperiment/cracks_tex.bmp"));
		ChunkMaterial.SetShaderParameter("_noise", noise);
		ChunkMaterial.SetShaderParameter("_spot_noise", spotnoise);
		ChunkMaterial.SetShaderParameter("_bordercol", fire_border_colour);
		ChunkMaterial.SetShaderParameter("_emissioncol", fire_emission_colour);
		ChunkMaterial.SetShaderParameter("_pulsecol", pulse_colour);
		ChunkMaterial.SetShaderParameter("_burncol", burned_colour);
		ChunkMaterial.SetShaderParameter("_acidcol", acid_colour);
		ChunkMaterial.SetShaderParameter("_acidedge", acid_edge);
		ChunkMaterial.SetShaderParameter("_pulse_when_damaged", false);
		ChunkMaterial.SetShaderParameter("_grass_lod_tex_array_pos", GetBlockTextureArrayPositions(BlockID("LODGrass"))[0]);
		ChunkMaterial.SetShaderParameter("_stone_lod_tex_array_pos", GetBlockTextureArrayPositions(BlockID("LODStone"))[0]);
		ChunkMaterial.SetShaderParameter("_leaves_lod_tex_array_pos", GetBlockTextureArrayPositions(BlockID("LODLeaves"))[0]);
		ChunkMaterial.SetShaderParameter("_dirt_lod_tex_array_pos", GetBlockTextureArrayPositions(BlockID("LODDirt"))[0]);

		ChunkMaterialDamagePulse = ChunkMaterial.Duplicate() as ShaderMaterial;
		ChunkMaterialDamagePulse.SetShaderParameter("_pulse_when_damaged", true);

		//BrokenBlockShader = ResourceLoader.Load("res://shaders/RIGID_BREAK_SHADER.tres") as ShaderMaterial;
		BrokenBlockShader = ResourceLoader.Load("res://shaders/broken_block_shader_wholeblock.tres") as ShaderMaterial;
		BrokenBlockShader.SetShaderParameter("_albedo", TextureArray);
		BrokenBlockShader.SetShaderParameter("_displacement", GD.Load("res://BlockTextures/textureExperiment/Ground_Dirt_006_DISPa.png"));
		BrokenBlockShader.SetShaderParameter("_roughness", GD.Load("res://BlockTextures/textureExperiment/Ground_Dirt_006_ROUGH.jpg"));
		BrokenBlockShader.SetShaderParameter("_normalmap", GD.Load("res://BlockTextures/textureExperiment/Ground_Dirt_006_NORM.jpg"));
		BrokenBlockShader.SetShaderParameter("_acidcurvetex", GD.Load("res://shaders/acid_damage_curve.tres"));
		BrokenBlockShader.SetShaderParameter("_firecurvetex", GD.Load("res://shaders/fire_damage_curve.tres"));
		BrokenBlockShader.SetShaderParameter("_cracks_texture", GD.Load("res://BlockTextures/textureExperiment/cracks_tex.bmp"));
		BrokenBlockShader.SetShaderParameter("_noise", noise);
		BrokenBlockShader.SetShaderParameter("_spot_noise", spotnoise);
		BrokenBlockShader.SetShaderParameter("_bordercol", fire_border_colour);
		BrokenBlockShader.SetShaderParameter("_emissioncol", fire_emission_colour);
		BrokenBlockShader.SetShaderParameter("_burncol", burned_colour);
		BrokenBlockShader.SetShaderParameter("_acidcol", acid_colour);
		BrokenBlockShader.SetShaderParameter("_acidedge", acid_edge);
		
		CellFractureBlockShader = ResourceLoader.Load("res://shaders/RIGID_BREAK_SHADER.tres") as ShaderMaterial;
		CellFractureBlockShader.SetShaderParameter("_albedo", TextureArray);
		CellFractureBlockShader.SetShaderParameter("_displacement", GD.Load("res://BlockTextures/textureExperiment/Ground_Dirt_006_DISPa.png"));
		CellFractureBlockShader.SetShaderParameter("_roughness", GD.Load("res://BlockTextures/textureExperiment/Ground_Dirt_006_ROUGH.jpg"));
		CellFractureBlockShader.SetShaderParameter("_normalmap", GD.Load("res://BlockTextures/textureExperiment/Ground_Dirt_006_NORM.jpg"));
		CellFractureBlockShader.SetShaderParameter("_acidcurvetex", GD.Load("res://shaders/acid_damage_curve.tres"));
		CellFractureBlockShader.SetShaderParameter("_firecurvetex", GD.Load("res://shaders/fire_damage_curve.tres"));
		CellFractureBlockShader.SetShaderParameter("_cracks_texture", GD.Load("res://BlockTextures/textureExperiment/cracks_tex.bmp"));
		CellFractureBlockShader.SetShaderParameter("_noise", noise);
		CellFractureBlockShader.SetShaderParameter("_spot_noise", spotnoise);
		CellFractureBlockShader.SetShaderParameter("_bordercol", fire_border_colour);
		CellFractureBlockShader.SetShaderParameter("_emissioncol", fire_emission_colour);
		CellFractureBlockShader.SetShaderParameter("_burncol", burned_colour);
		CellFractureBlockShader.SetShaderParameter("_acidcol", acid_colour);
		CellFractureBlockShader.SetShaderParameter("_acidedge", acid_edge);

        DestructibleObjectShader = ResourceLoader.Load("res://shaders/breakable_object.tres") as ShaderMaterial;
		DestructibleObjectShader.SetShaderParameter("_displacement", GD.Load("res://BlockTextures/textureExperiment/Ground_Dirt_006_DISPa.png"));
		DestructibleObjectShader.SetShaderParameter("_roughness", GD.Load("res://BlockTextures/textureExperiment/Ground_Dirt_006_ROUGH.jpg"));
		DestructibleObjectShader.SetShaderParameter("_normalmap", GD.Load("res://BlockTextures/textureExperiment/Ground_Dirt_006_NORM.jpg"));
		DestructibleObjectShader.SetShaderParameter("_acidcurvetex", GD.Load("res://shaders/acid_damage_curve.tres"));
		DestructibleObjectShader.SetShaderParameter("_firecurvetex", GD.Load("res://shaders/fire_damage_curve.tres"));
		DestructibleObjectShader.SetShaderParameter("_cracks_texture", GD.Load("res://BlockTextures/textureExperiment/cracks_tex.bmp"));
		DestructibleObjectShader.SetShaderParameter("_noise", noise);
		DestructibleObjectShader.SetShaderParameter("_spot_noise", spotnoise);
		DestructibleObjectShader.SetShaderParameter("_bordercol", fire_border_colour);
		DestructibleObjectShader.SetShaderParameter("_emissioncol", fire_emission_colour);
		DestructibleObjectShader.SetShaderParameter("_burncol", burned_colour);
		DestructibleObjectShader.SetShaderParameter("_acidcol", acid_colour);
		DestructibleObjectShader.SetShaderParameter("_acidedge", acid_edge);
		DestructibleObjectShader.SetShaderParameter("_fuse_pulse_curve", GD.Load("res://shaders/pulse_damage_curve.tres"));
		DestructibleObjectShader.SetShaderParameter("_fuse_pulse_colour", new Color(1.0f, 1.0f, 1.0f));
		DestructibleObjectShader.SetShaderParameter("_fuse_ratio", 0.0f);
		DestructibleObjectShader.SetShaderParameter("_fuse_is_active", false);

		 // Save the image to a file (PNG format)
        /*
		GD.Print($"Block textures: {blockTextures.Length}");
		GD.Print($"Block images: {blockImages.Count}");
		GD.Print($"Texture array size: {TextureArray.GetLayers()}");
		GD.Print($"Done loading {blockTextures.Length} images to make {TextureArray.GetLayers()} sized texture array");
		*/
		
		/*for (int i=0; i< tex_array.GetLayers(); i++) {
			GD.Print(tex_array.GetLayerData(i));
			string path = $"user://texture_for_array_layer_{i}.png";
			var error = tex_array.GetLayerData(i).SavePng(path);
			if (error == Error.Ok) GD.Print($"Image saved successfully to {path}");
			else  GD.PrintErr($"Failed to save image: {error}");
		}*/
	}
}
