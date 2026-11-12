shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;
uniform vec4 base_tint = vec4(.25,.34,.33,1.0);
uniform float metallic_amount = .3;
uniform float polish = .5;
varying vec3 fabrication_world;
varying vec3 fabrication_normal;
varying vec4 fabrication_colour;

void vertex() {
    fabrication_world = (MODEL_MATRIX * vec4(VERTEX,1.0)).xyz;
    fabrication_normal = normalize((MODEL_MATRIX * vec4(NORMAL,0.0)).xyz);
    fabrication_colour = dot(COLOR.rgb,vec3(1.0))<.01 ? vec4(1.0) : COLOR;
}

void fragment() {
    // The grain lives in metres, so every sleeve, screw and cabinet shares
    // its finish instead of stretching the same tiny texture over each box.
    float footprint = max(length(dFdx(fabrication_world)),length(dFdy(fabrication_world)));
    float grain_visibility = 1.0-smoothstep(.006,.035,footprint);
    float brush = sin((fabrication_world.y+fabrication_world.x*.11)*570.0)*.035*grain_visibility;
    float cast = sin(dot(fabrication_world,vec3(41.7,23.6,37.4)))*.022;
    float finish = .94+brush+cast;
    vec3 surface = base_tint.rgb * finish * fabrication_colour.rgb;
    ALBEDO = surface;
    METALLIC = metallic_amount;
    ROUGHNESS = clamp(polish+brush*1.8+cast,.28,.83);
    SPECULAR = .38;
    // A directional, texture-matched ambient fill retains readable bevels in
    // Compatibility, whose baked probe support differs from Forward+.
    float upper = max(dot(normalize(fabrication_normal),normalize(vec3(-.45,.82,.36))),0.0);
    EMISSION = surface * (.14+.31*upper);
}
