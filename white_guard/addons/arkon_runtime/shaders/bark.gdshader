shader_type spatial;
render_mode cull_back,vertex_lighting;
uniform sampler2D bark : source_color,filter_linear_mipmap_anisotropic,repeat_enable;
void fragment(){ALBEDO=texture(bark,UV*vec2(1.0,.7)).rgb*vec3(.62,.54,.44);ROUGHNESS=.95;SPECULAR=.0;}
