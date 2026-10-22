extends Node
# Home-owned adapter for reviewed Godot 3 browser builds, not an emulator.
var clock := 0.0
var last_payload := ""
var resumed := false
var sequence := 0
var next_capture := 2.0
var arcade_time := 0.0
var was_paused := true

func _ready():
	pause_mode = Node.PAUSE_MODE_PROCESS
	if OS.get_name() != "HTML5":
		set_process(false)
		return
	var initial = JavaScript.eval("JSON.stringify(window.homeGame.initialFiles || {})", true)
	if initial is String:
		var parsed = JSON.parse(initial)
		if parsed.error == OK and parsed.result is Dictionary:
			for path in allowed_paths():
				if parsed.result.has(path) and parsed.result[path] is String:
					var dir = Directory.new()
					dir.make_dir_recursive("user://" + path.get_base_dir())
					var file = File.new()
					if file.open("user://" + path, File.WRITE) == OK:
						file.store_string(parsed.result[path])
						file.close()
	JavaScript.eval("window.homeGame.engineReady = true", true)

func allowed_paths():
	if ProjectSettings.get_setting("application/config/name") == "Tiny Crate":
		return ["save/0/box.save", "save/1/box.save", "save/2/box.save", "save/3/box.save", "save/4/box.save", "save/5/box.save"]
	return ["home-progress.json"]

func _process(delta):
	clock += delta
	var paused = JavaScript.eval("window.homeGame.paused === true", true)
	get_tree().paused = paused == true
	if get_tree().paused != was_paused:
		for action in InputMap.get_actions():
			Input.action_release(action)
		was_paused = get_tree().paused
	AudioServer.set_bus_mute(0, get_tree().paused)
	var next_sequence = JavaScript.eval("window.homeGame.saveSequence || 0", true)
	if clock < 1.5:
		return
	if !resumed:
		if ProjectSettings.get_setting("application/config/name") == "Wrap It Up":
			var shared = get_node_or_null("/root/Shared")
			if shared != null and is_instance_valid(shared.arcade) and !get_tree().paused:
				arcade_time += delta
			if arcade_time < 1.3:
				JavaScript.eval("window.homeGame.savedSequence = window.homeGame.saveSequence", true)
				return
		resumed = true
		resume_wrap()
	if clock >= next_capture or next_sequence != sequence:
		sequence = int(next_sequence)
		next_capture = clock + 2.0
		capture()

func resume_wrap():
	if ProjectSettings.get_setting("application/config/name") != "Wrap It Up":
		return
	var shared = get_node_or_null("/root/Shared")
	if shared == null or !is_instance_valid(shared.arcade):
		resumed = false
		return
	var file = File.new()
	if file.open("user://home-progress.json", File.READ) != OK:
		return
	var parsed = JSON.parse(file.get_as_text())
	file.close()
	if parsed.error != OK or !(parsed.result is Dictionary):
		return
	var saved = parsed.result
	if !saved.has("map") or !saved.has("maps") or !(saved.maps is Array):
		return
	var actual = shared.list_all_files(shared.arcade.folder)
	if saved.maps.size() != actual.size():
		return
	for path in saved.maps:
		if !(path is String) or !actual.has(path):
			return
	shared.arcade.maps = saved.maps
	shared.arcade.map = clamp(int(saved.map), 0, 10000)
	shared.save_time = max(0, float(saved.get("time", 0)))
	shared.arcade.scene()

func capture():
	var shared = get_node_or_null("/root/Shared")
	if shared == null:
		return
	if ProjectSettings.get_setting("application/config/name") == "Tiny Crate":
		# Upstream uses completed-level save data; this is not a full save state.
		if shared.is_in_game or shared.is_level_select:
			shared.save()
	else:
		if is_instance_valid(shared.arcade):
			shared.file_save_json("user://home-progress.json", {"map": shared.arcade.map, "maps": shared.arcade.maps, "time": shared.save_time})
	var files := {}
	for path in allowed_paths():
		var file = File.new()
		if file.open("user://" + path, File.READ) == OK:
			if file.get_len() <= 32768:
				files[path] = file.get_as_text()
			file.close()
	var payload = JSON.print(files)
	if payload != last_payload:
		last_payload = payload
		JavaScript.eval("window.homeGame.save(" + JSON.print(payload) + ")", true)
	JavaScript.eval("window.homeGame.savedSequence = window.homeGame.saveSequence", true)
