extends RefCounted
## Hardware selection is a conservative starting point, not a measured benchmark.

static func classify_hardware(info: Dictionary) -> Dictionary:
    if str(info.get("platform", "")).to_lower() == "windows":
        return _classify_windows(info)
    var gpu := str(info.get("gpu_name", "")).to_lower()
    for token in ["(tm)", "™", "-", "_", "(", ")"]: gpu = gpu.replace(token, " ")
    var compact := gpu.replace(" ", "")
    var memory := float(info.get("memory_bytes", 0))
    var known := memory > 0
    var gb := memory / 1073741824.0 if known else 0.0
    var mobile := str(info.get("platform", "")).to_lower() in ["android", "ios"]
    var software := bool(info.get("headless", false)) or bool(info.get("software_renderer", false)) or "llvmpipe" in gpu or "softpipe" in gpu or "swiftshader" in gpu
    var result := {"profile":"high", "reason":"Настройки для компьютера", "target_fps":60, "memory_known":known, "memory_gb":gb, "software":software}
    if not mobile:
        if bool(info.get("headless", false)) or software: result.reason = "Программный рендер: скорость телефона не измеряется"
        return result
    var weak := software
    for family in ["malig31", "malig51", "malig52", "malig57", "powervrge", "powervrroguege", "adreno505", "adreno506", "adreno508", "adreno509", "adreno510", "adreno512", "adreno530", "adreno540", "adreno610", "adreno612", "adreno613", "adreno615", "adreno616", "adreno618", "adreno619", "adreno620"]:
        if family in compact: weak = true
    var strong := false
    for family in ["adreno650", "adreno660", "adreno730", "adreno740", "adreno750", "adreno830", "adreno840", "malig710", "malig715", "malig720", "immortalis"]:
        if family in compact: strong = true
    var moderate := false
    for family in ["adreno630", "adreno640", "adreno642", "adreno644", "adreno710", "adreno720", "malig68", "malig76", "malig77", "malig78", "malig610"]:
        if family in compact: moderate = true
    if weak or (known and gb <= 4.5):
        result.profile = "low"
        result.reason = "Экономный профиль: мобильный GPU / объём памяти"
    elif strong and known and gb >= 5.0:
        result.reason = "Мощный мобильный GPU и достаточная память"
    elif strong:
        result.profile = "balanced"
        result.reason = "Мощный GPU; запас памяти не подтверждён"
    elif moderate and known and gb >= 5.0:
        result.profile = "balanced"
        result.reason = "Средний мобильный GPU и достаточная память"
    else:
        # Unknown Android hardware starts safely, including Redmi regional names.
        result.profile = "low"
        result.reason = "Безопасный профиль для неопознанного мобильного GPU"
    result.target_fps = 30 if result.profile == "low" else 60
    return result

static func is_software_renderer(gpu_name: String, platform: String) -> bool:
    var gpu := gpu_name.to_lower()
    for token in ["llvmpipe", "softpipe", "swiftshader"]:
        if token in gpu: return true
    if platform.to_lower() == "windows":
        var compact := "".join(_windows_gpu_words(gpu))
        for token in ["microsoftbasicrenderdriver", "microsoftbasicdisplayadapter", "gdigeneric", "warprenderer", "softwarerasterizer"]:
            if token in compact: return true
    return false

static func _windows_gpu_words(gpu_name: String) -> PackedStringArray:
    var normalized := gpu_name.to_lower()
    for token in ["(r)", "®", "(tm)", "™", "-", "_", "(", ")"]:
        normalized = normalized.replace(token, " ")
    # Driver strings vary between HD630/HD630 Graphics, GTX1050Ti and names
    # with trademark words. Split known brands/families before reading models.
    for word in ["nvidia", "geforce", "amd", "radeon", "intel", "graphics", "rtx", "gtx", "iris", "uhd", "vega", "arc"]:
        normalized = normalized.replace(word, " " + word + " ")
    var tokenizer := RegEx.new()
    tokenizer.compile("[a-z]+|[0-9]+")
    var words := PackedStringArray()
    for item in tokenizer.search_all(normalized): words.append(item.get_string())
    return words

static func _gpu_model_after(words: PackedStringArray, family: String) -> int:
    var index := words.find(family)
    if index < 0: return -1
    for position in range(index + 1, mini(words.size(), index + 5)):
        if words[position].is_valid_int(): return int(words[position])
    return -1

static func _classify_windows(info: Dictionary) -> Dictionary:
    var memory := float(info.get("memory_bytes", 0))
    var known := memory > 0
    var gb := memory / 1073741824.0 if known else 0.0
    var gpu := str(info.get("gpu_name", ""))
    var headless := bool(info.get("headless", false))
    var software := headless or bool(info.get("software_renderer", false)) or is_software_renderer(gpu, "Windows")
    var result := {"profile":"balanced", "reason":"Безопасный профиль для неопознанного GPU Windows", "target_fps":60, "memory_known":known, "memory_gb":gb, "software":software}
    if headless:
        # Exported headless acceptance checks have no hardware performance data.
        result.profile = "high"
        result.reason = "Headless: производительность видеокарты не измеряется"
        return result
    if software or (known and gb <= 4.5):
        result.profile = "low"
        result.reason = "Экономный профиль: программный рендер или небольшой объём памяти"
        result.target_fps = 30
        return result
    var words := _windows_gpu_words(gpu)
    var intel := words.has("intel") or "intel" in str(info.get("gpu_vendor", "")).to_lower()
    var amd := words.has("amd") or words.has("radeon")
    var gtx := _gpu_model_after(words, "gtx")
    var rtx := _gpu_model_after(words, "rtx")
    var rx := _gpu_model_after(words, "rx")
    var vega := _gpu_model_after(words, "vega")
    var radeon := _gpu_model_after(words, "radeon")
    var arc := _gpu_model_after(words, "arc")
    var weak := (intel and not words.has("iris") and not words.has("arc")) or (intel and words.has("iris") and words.has("pro"))
    weak = weak or words.has("gt") or words.has("mx") or (gtx > 0 and gtx < 750)
    weak = weak or (amd and (vega == 3 or (words.has("m") and radeon == 610)))
    weak = weak or (amd and words.has("r") and (words.has("5") or words.has("7")))
    var strong := gtx in [970,980,1060,1070,1080,1650,1660]
    strong = strong or rtx in [2060,2070,2080,3050,3060,3070,3080,3090,4050,4060,4070,4080,4090,5050,5060,5070,5080,5090]
    strong = strong or rx in [470,480,570,580,590,5300,5500,5600,5700,6400,6500,6600,6700,6800,6900,7600,7700,7800,7900,9060,9070]
    strong = strong or (amd and vega in [56,64])
    strong = strong or (intel and words.has("arc") and ((words.has("a") and arc in [580,750,770]) or (words.has("b") and arc in [570,580])))
    if weak:
        result.profile = "low"
        result.reason = "Экономный профиль для встроенной или старой видеокарты"
    elif strong and known and gb >= 7.0:
        result.profile = "high"
        result.reason = "Известная видеокарта и достаточная память Windows"
    elif strong:
        result.reason = "Известная видеокарта; запас памяти не подтверждён"
    elif intel or amd or gtx > 0 or rtx > 0 or rx > 0:
        result.reason = "Сбалансированный профиль для GPU Windows"
    result.target_fps = 30 if result.profile == "low" else 60
    return result

static func level_settings(profile: String, level_id: StringName, dynamic_step: int = 0, platform: String = "") -> Dictionary:
    var tier := ["low", "balanced", "high"].find(profile)
    if tier < 0: tier = 0
    var step := clampi(dynamic_step, 0, 2)
    var out := {"profile":["low","balanced","high"][tier], "base_profile":["low","balanced","high"][tier], "dynamic_step":step, "target_fps":30 if tier == 0 else 60, "msaa_3d":1 if tier == 2 else 0, "mesh_lod_threshold":[2.5,1.7,1.0][tier]}
    if level_id == &"foot":
        var budgets := [409920,921600,2073600] if platform.to_lower() == "windows" else [409920,655360,921600]
        out.merge({"pixel_budget":budgets[tier], "moon_shadows":tier > 0, "shadow_distance":[0.0,30.0,55.0][tier], "material_normal_distance":[0.0,8.0,14.0][tier], "decorative_lights":tier > 0, "decorative_light_range_scale":[0.0,0.7,1.0][tier]})
        if tier == 0: out.pixel_budget = [409920,291600,230400][step]
    elif level_id == &"vehicle":
        out.merge({"car_pixel_budget":[409920,655360,0][tier], "car_msaa":1 if tier==0 else 2, "road_rows":[96,112,128][tier], "scenery_range":[320.0,400.0,480.0][tier], "scenery_tunnel_range":[160.0,192.0,224.0][tier], "scenery_detail_cutoff":[64.0,96.0,130.0][tier], "rotor_blur":tier > 0})
        out.msaa_3d = 0
        if tier == 0:
            out.car_pixel_budget = [409920,307200,230400][step]
            out.road_rows = [96,88,80][step]
            out.scenery_range = [320.0,288.0,256.0][step]
    elif level_id == &"interior":
        out.merge({"l3_real_lights":[4,8,14][tier], "rain_enabled":tier > 0, "rain_quality":tier, "hit_particle_limit":[48,72,96][tier], "hit_drops":[6,10,15][tier], "stain_limit":[120,240,360][tier], "minimap_interval":[0.2,0.15,0.1][tier]})
        if tier == 0:
            out.l3_real_lights = [4,3,2][step]
            out.hit_drops = [6,5,4][step]
            out.stain_limit = [120,100,80][step]
    # Medium/high can also shed visual work without altering simulation.
    if tier > 0 and step > 0:
        var lower := level_settings(["low","balanced","high"][tier - 1], level_id, step - 1, platform)
        lower.base_profile = out.base_profile
        lower.dynamic_step = step
        # Reaching the low tier also adopts its stable30FPS target. A capped
        #30FPS result can never upgrade this back to60 automatically.
        return lower
    return out
