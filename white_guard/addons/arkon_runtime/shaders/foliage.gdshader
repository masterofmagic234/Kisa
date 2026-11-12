shader_type spatial;
render_mode cull_back;
uniform sampler2D leaves : source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform vec3 tint : source_color=vec3(.65,.73,.47);
uniform bool golden=false;
void vertex(){vec3 w=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;VERTEX.x+=sin(TIME*.8+w.x*.11+w.z*.16)*.008;}
void fragment(){vec3 c=texture(leaves,UV*vec2(2.0,1.4)).rgb;ALBEDO=c*(golden ? vec3(.89,.70,.30) : tint);ROUGHNESS=.98;SPECULAR=.0;}
