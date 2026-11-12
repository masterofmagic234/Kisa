shader_type spatial;
render_mode cull_back;
uniform sampler2D meadow : source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D leaf_litter : source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform bool path_surface=false;
uniform bool clearing_surface=false;
uniform sampler2D contact_shade : filter_linear,repeat_disable;
varying vec3 world_position;
void vertex(){world_position=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;}
void fragment(){
    vec2 p=world_position.xz;
    vec3 grass=texture(meadow,p*.30).rgb;
    vec3 litter=texture(leaf_litter,p*.20).rgb;
    vec3 ground=mix(grass,litter,.33)*vec3(.78,.81,.67);
    vec3 gravel=mix(vec3(.40,.32,.22),litter*vec3(.87,.69,.46),.55);
    float path_mix=1.0-smoothstep(.31,.50,abs(UV.x-.5));
    if(clearing_surface){path_mix=1.0-smoothstep(.63,.92,length(UV-vec2(.5))*2.0);}
    ALBEDO=mix(ground,gravel,path_surface ? path_mix : 0.0)*texture(contact_shade,(p+vec2(38.0,32.0))/vec2(76.0,64.0)).r;
    ROUGHNESS=.97;SPECULAR=0.0;
}
