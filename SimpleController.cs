using Godot;
using System;

public partial class SimpleController : CharacterBody3D
{
	[Export] public Camera3D Camera;
	[Export] public float MoveSpeed {get;set;} = 5.0f;
	[Export] public float MoveLerpFactor {get;set;} = 3.0f;
	private bool _control_active = true;
	private Node3D _camera_gimbal;
	private Node3D _camera_container;
	private float _mouse_sen = 0.3f;
	private float _cameraXRotation = 0.0f;
	
	public override void _Ready()
	{
		Input.SetMouseMode(Input.MouseModeEnum.Captured);
		_camera_gimbal = new Node3D();
		_camera_container = new Node3D();
		_camera_container.AddChild(_camera_gimbal);
		AddChild(_camera_container);
		_camera_container.GlobalTransform = Camera.GlobalTransform;
		Camera.Reparent(_camera_gimbal);
	}

	public override void _Input(InputEvent @event)
	{

		if (@event is InputEventKey)
		{
			var keyEvent = @event as InputEventKey;
			if (keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
			{
				if (Input.GetMouseMode() == Input.MouseModeEnum.Visible)
				{
					Input.SetMouseMode(Input.MouseModeEnum.Captured);
					_control_active = true;
				}
				else
				{
					Input.SetMouseMode(Input.MouseModeEnum.Visible);
					_control_active = false;
				}
			}
			else if (keyEvent.Pressed && keyEvent.Keycode == Key.F1)
			{
				if (GetViewport().DebugDraw==Viewport.DebugDrawEnum.Wireframe) {
					RenderingServer.SetDebugGenerateWireframes(false);
					GetViewport().DebugDraw=Viewport.DebugDrawEnum.Disabled;
				} else {
					RenderingServer.SetDebugGenerateWireframes(true);
					GetViewport().DebugDraw=Viewport.DebugDrawEnum.Wireframe;
				}
			}
		}

		if (!_control_active) return;

		if (@event is InputEventMouseMotion)
		{
			var mouseMotion = @event as InputEventMouseMotion;
			var deltaX = mouseMotion.Relative.Y * _mouse_sen;
			var deltaY = -mouseMotion.Relative.X * _mouse_sen;

			_camera_gimbal.RotateY(Mathf.DegToRad(deltaY));
			if (_cameraXRotation + deltaX > -90 && _cameraXRotation + deltaX < 90)
			{
				Camera.RotateX(Mathf.DegToRad(-deltaX));
				_cameraXRotation += deltaX;
			}
		}

		
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_control_active) return;

		var inputDirection = Input.GetVector("Left", "Right", "Back", "Forward");
		var y_input = ((Input.IsActionPressed("Jump") ? 1.0f : 0.0f) - (Input.IsActionPressed("Crouch") ? 1.0f : 0.0f)) * Vector3.Up;

		var direction = new Vector3(inputDirection.X, 0.0f, -inputDirection.Y);
		direction = Camera.GlobalTransform.Basis * direction.Normalized() + y_input;

		var velocity = Velocity;

		velocity.X = Mathf.Lerp(velocity.X, direction.X * MoveSpeed, (float)delta * MoveLerpFactor);
		velocity.Y = Mathf.Lerp(velocity.Y, direction.Y * MoveSpeed, (float)delta * MoveLerpFactor);
		velocity.Z = Mathf.Lerp(velocity.Z, direction.Z * MoveSpeed, (float)delta * MoveLerpFactor);

		Velocity = velocity;

		MoveAndSlide();
	}
}
