shader_type canvas_item;
render_mode unshaded;

uniform vec4 shadow_color : source_color = vec4(0.12, 0.28, 0.85, 1.0);
uniform vec4 highlight_color : source_color = vec4(0.38, 0.95, 1.0, 1.0);
uniform float opacity : hint_range(0.0, 1.0) = 0.68;

void fragment() {
	vec4 source = texture(TEXTURE, UV) * COLOR;
	float luminance = dot(source.rgb, vec3(0.299, 0.587, 0.114));
	float highlight = smoothstep(0.08, 0.8, luminance);
	vec3 afterimage_color = mix(shadow_color.rgb, highlight_color.rgb, highlight);
	COLOR = vec4(afterimage_color, source.a * opacity);
}
