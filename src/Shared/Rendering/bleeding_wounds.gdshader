shader_type canvas_item;
render_mode unshaded;

uniform vec4 wound_color : source_color = vec4(0.72, 0.015, 0.02, 0.9);
uniform vec2 frame_grid = vec2(1.0);
uniform float coverage : hint_range(0.01, 1, 0.01) = 0.3;
uniform float seed = 0.0;

float hash(vec2 value) {
	vec2 shifted = value + vec2(seed, seed * 0.371);
	return fract(sin(dot(shifted, vec2(127.1, 311.7))) * 43758.5453);
}

void fragment() {
	vec4 source = COLOR;
	vec2 frame_uv = fract(UV * frame_grid);
	vec2 frame_pixel_count = 1.0 / (TEXTURE_PIXEL_SIZE * frame_grid);
	vec2 pixel = floor(frame_uv * frame_pixel_count);

	// One stable roll per 2x2 patch makes wounds clustered instead of TV static.
	float patch = step(1.0 - coverage, hash(floor(pixel / 2.0)));
	float broken_edge = step(0.18, hash(pixel + vec2(43.7, 19.1)));
	float colored_pixel = step(0.08, max(source.r, max(source.g, source.b)));
	float alpha = patch * broken_edge * colored_pixel * source.a * wound_color.a;
	COLOR = vec4(wound_color.rgb, alpha);
}
