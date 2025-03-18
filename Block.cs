using Godot;
using System;


// each block has 6 textures from 0-5
// order: bottom, top, left, right, back, front 
// NOTE that the "back" texture faces in the -z direction (which is "forward" in Godot's physics)
// the "front" texture faces in the +z direction (which is "back" in Godot's physics)
// this is because we construct the blocks in "chunk space" and don't transform the texture map to godot's physics space

// blocks have 5 damage bits (32 levels of damage) and 3 damage types (stored as bit flags)
// Their "fragility" determines how easily broken they are. Invincible blocks have fragility 0.
[Tool]
[GlobalClass]
public partial class Block : Resource
{
	// block ID is a unique number between 0-1024
	[Export] public string Name { get; set; }
	[Export] public BlockSpecies Species { get; set; }

	public float Fragility { get => GetFragility(Species); }

	[Export] public Texture2D MidTexture { get => _midTexture; set {_midTexture = value; SetTextures();} }
	private Texture2D _midTexture;
	[Export] public Texture2D BottomTexture { get => _bottomTexture; set {_bottomTexture = value; SetTextures();} }
	private Texture2D _bottomTexture;
	[Export] public Texture2D TopTexture { get => _topTexture; set {_topTexture = value; SetTextures();} }
	private Texture2D _topTexture;

	[ExportCategory("Face Textures")]
	[Export] public Texture2D LeftTexture { get => _leftTexture; set {_leftTexture = value; SetTextures();} }
	private Texture2D _leftTexture;
	[Export] public Texture2D RightTexture { get => _rightTexture; set {_rightTexture = value; SetTextures();} }
	private Texture2D _rightTexture;
	[Export] public Texture2D BackTexture { get => _backTexture; set {_backTexture = value; SetTextures();} }
	private Texture2D _backTexture;
	[Export] public Texture2D FrontTexture { get => _frontTexture; set {_frontTexture = value; SetTextures();} }
	private Texture2D _frontTexture;
	public Texture2D[] Textures {get => _textures; private set {_textures = value;}}
	private Texture2D[] _textures = new Texture2D[6];

	public int[] BakedTextureArrayPositions {get => _bakedTextureArrayPositions; set {_bakedTextureArrayPositions = value;}}
	private int[] _bakedTextureArrayPositions;

	public void SetTextures() {
		if (MidTexture!= null && BottomTexture!= null && TopTexture!=null) {
			Textures = new Texture2D[] { BottomTexture,TopTexture,MidTexture,MidTexture,MidTexture,MidTexture };
		}
		else if (MidTexture!= null) {
			Textures = new Texture2D[] { MidTexture,MidTexture,MidTexture,MidTexture,MidTexture,MidTexture};
		}
		else {
			Textures = new Texture2D[] { BottomTexture,TopTexture,LeftTexture,RightTexture,BackTexture,FrontTexture };
		}
	}

	// blocks have 5 damage bits (32 levels of damage) and 3 damage types (stored as bit flags)
	// Their "fragility" determines how easily broken they are. Invincible blocks have fragility 0.
	public static float GetFragility(BlockSpecies species) {
		return species switch {
			BlockSpecies.Air => 0.0f,
			BlockSpecies.Lava => 0.0f,
			BlockSpecies.Stone => 0.25f,
			BlockSpecies.Porcelain => 0.8f,
			BlockSpecies.Dirt => 1.2f,
			BlockSpecies.Grass => 1.3f,
			BlockSpecies.Gravel => 1f,
			BlockSpecies.Wood => 0.6f,
			BlockSpecies.Leaves => 31.0f,
			BlockSpecies.Brick => 0.4f,
			BlockSpecies.GoldOre => 0.5f,
			_ => 1.0f
		};
	}
}