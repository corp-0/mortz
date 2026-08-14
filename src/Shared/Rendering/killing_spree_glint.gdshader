// Adapted from "Gradient pixel glint" by shadecore_dev, CC0.
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D glint_gradient : source_color, filter_nearest, repeat_disable;
uniform vec2 frame_grid = vec2(1.0);
uniform float sweep_duration : hint_range(0.1, 0.6, 0.01) = 0.32;
uniform float pause_duration : hint_range(0.2, 3.0, 0.05) = 0.75;
uniform float shine_size : hint_range(0.01, 0.5, 0.01) = 0.1;
uniform float shine_angle : hint_range(0.0, 89.9, 0.1) = 45.0;

float remap(float value, float in_min, float in_max, float out_min, float out_max) {
	return (value - in_min) * (out_max - out_min) / (in_max - in_min) + out_min;
}

void fragment() {
	vec4 source = COLOR;
	float cycle_time = mod(TIME, sweep_duration + pause_duration);
	float shine_progress = clamp(cycle_time / sweep_duration, 0.0, 1.0);
	float shine_strength = cycle_time < sweep_duration ? 1.0 : 0.0;
	vec2 frame_uv = fract(UV * frame_grid);
	vec2 frame_pixel_count = 1.0 / (TEXTURE_PIXEL_SIZE * frame_grid);
	vec2 pixelated_uv = floor(frame_uv * frame_pixel_count) / frame_pixel_count;
	float slope = tan(radians(shine_angle));
	float progress = remap(
		shine_progress,
		0.0,
		1.0,
		-1.0 - shine_size - shine_size * slope,
		slope + shine_size + shine_size * slope);
	float distance_to_shine =
		(slope * pixelated_uv.x - pixelated_uv.y - progress) /
		sqrt(slope * slope + 1.0);
	float gradient_uv = clamp(distance_to_shine / (2.0 * shine_size) + 0.5, 0.0, 1.0);
	vec4 glint = texture(glint_gradient, vec2(gradient_uv, 0.5));
	float band = step(distance_to_shine, shine_size) - step(distance_to_shine, -shine_size);
	float alpha = band * glint.a * shine_strength * source.a;
	COLOR = vec4(glint.rgb, alpha);
}
