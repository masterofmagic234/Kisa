shader_type spatial;
render_mode cull_back, unshaded, specular_disabled;
#include "res://addons/arkon_runtime/shaders/garden_color.gdshaderinc"
uniform sampler2D panorama : filter_linear_mipmap, repeat_enable;
varying vec3 world_position;
void vertex() { world_position=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz; }
void fragment() {
    vec2 p=world_position.xz;
    float a=p.x*2.7+p.y*1.9+TIME*.65;
    float b=p.x*-1.4+p.y*3.6-TIME*.43;
    vec3 n=normalize(vec3(-cos(a)*.045+cos(b)*.022,1.0,-cos(a)*.032-cos(b)*.057));
    vec3 view=normalize(CAMERA_POSITION_WORLD-world_position);
    vec3 ray=reflect(-view,n);
    vec2 sky_uv=vec2(atan(ray.x,ray.z)*.159154943+.5,acos(clamp(ray.y,-1.0,1.0))*.318309886);
    vec3 sky=max(textureLod(panorama,sky_uv,1.0).rgb,vec3(0.0));
    sky=sky/(vec3(1.0)+sky);
    float edge=clamp(1.0-length((p-vec2(0.0,23.0))/vec2(18.5,9.5)),0.0,1.0);
    vec3 water=mix(vec3(.065,.17,.13),vec3(.018,.075,.067),smoothstep(0.0,.35,edge));
    float f=1.0-max(dot(n,view),0.0);float f2=f*f;
    vec3 color=mix(water,sky,.055+.47*f2*f2*f);
    color+=vec3(.012,.018,.012)*(sin(a)*sin(b)*.5+.5);
    ALBEDO=garden_from_linear(color);
}
