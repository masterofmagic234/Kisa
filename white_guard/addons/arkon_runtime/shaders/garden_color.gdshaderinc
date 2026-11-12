// sRGB approximations from Godot (MIT); see GODOT_MATERIAL_LICENSE.txt.
// Forward+ supplies source_color samples/uniforms in linear space. The GL
// renderer supplies sRGB and converts ALBEDO after fragment(). Keep products
// and texture blends in the same linear space on both renderers.
vec3 garden_to_linear(vec3 color) {
    return OUTPUT_IS_SRGB ? color * (color * (color * .305306011 + .682171111) + .012522878) : color;
}
vec3 garden_from_linear(vec3 color) {
    return OUTPUT_IS_SRGB ? max(vec3(1.055) * pow(max(color, vec3(0.0)), vec3(.416666667)) - .055, vec3(0.0)) : color;
}
