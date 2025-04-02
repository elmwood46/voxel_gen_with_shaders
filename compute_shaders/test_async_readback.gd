extends Node3D

var rd
var in_counter
var out_counter
var buffer
var in_array
var out_array
var data
var format
var view
var texture
var texture_values

func _init():
	rd = RenderingServer.get_rendering_device()
	buffer = rd.storage_buffer_create(4)
	in_counter = 0;
	in_array = PackedByteArray()
	in_array.resize(4)
	
	data = [ PackedByteArray() ]
	format = RDTextureFormat.new()
	format.width = 4
	format.height = 4
	format.format = RenderingDevice.DATA_FORMAT_R8_UINT
	format.usage_bits = RenderingDevice.TEXTURE_USAGE_SAMPLING_BIT | RenderingDevice.TEXTURE_USAGE_CAN_COPY_FROM_BIT
	
	view = RDTextureView.new()
	data[0].resize(16)
	data[0].encode_u32(0, 0xFF0000FF)
	data[0].encode_u32(4, 0xFF0000FF)
	data[0].encode_u32(8, 0x00FF00FF)
	data[0].encode_u32(12, 0x0000FFFF)
	texture = rd.texture_create(format, view, data)
	texture_values = [0, 0, 0, 0]
	pass

func _buffer_get_data_callback(array):
	out_counter = array.decode_u32(0)
	pass
	
func _texture_get_data_callback(array):
	texture_values[0] = array.decode_u32(0)
	texture_values[1] = array.decode_u32(4)
	texture_values[2] = array.decode_u32(8)
	texture_values[3] = array.decode_u32(12)
	pass

func _process(_delta):
	in_counter += 1
	in_array.encode_u32(0, in_counter)
	rd.buffer_update(buffer, 0, in_array.size(), in_array)
	rd.buffer_get_data_async(buffer, _buffer_get_data_callback, 0, in_array.size())
	rd.texture_get_data_async(texture, 0, _texture_get_data_callback)
	
	$Label.text = "Counter: %X Buffer Value: %X\n(these shouldn't match if it's async)\nTexture Values: %X %X %X %X" % [in_counter, out_counter, texture_values[0], texture_values[1], texture_values[2], texture_values[3]]
	
	pass
