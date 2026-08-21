shader_type canvas_item;

varying vec4 brush_data;

void vertex() {
    brush_data = COLOR;
}

void fragment() {
    bool ellipse = abs(brush_data.b - 0.125) < 0.01 &&
        abs(brush_data.a - 0.625) < 0.01;
    vec2 local = brush_data.rg * 2.0 - 1.0;
    if (ellipse && dot(local, local) > 1.0) {
        discard;
    }
    ivec2 texture_size = textureSize(TEXTURE, 0);
    ivec2 texel = ivec2(floor(UV * vec2(texture_size)));
    texel = ivec2(
        (texel.x % texture_size.x + texture_size.x) % texture_size.x,
        (texel.y % texture_size.y + texture_size.y) % texture_size.y);
    vec4 tint = ellipse ? vec4(1.0) : brush_data;
    COLOR = texelFetch(TEXTURE, texel, 0) * tint;
}
