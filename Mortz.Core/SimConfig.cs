namespace Mortz.Core;

/// <summary>
/// Simulation constants. Units are pixels and seconds; +Y is down (screen space),
/// matching Godot 2D so positions pass through the shell unconverted.
/// Game feel is tuned here. Durations are in seconds; the *Ticks values the
/// sim consumes are derived at compile time, don't edit those.
/// Arena dimensions come from the loaded map.
/// </summary>
public static class SimConfig
{
    public const int TICK_RATE = 60;
    public const float DT = 1f / TICK_RATE;

    public const float PLAYER_HALF_WIDTH = 16;
    public const float PLAYER_HALF_HEIGHT = 16;

    /// <summary>Max wall height (px) walked over automatically; makes carved rubble traversable.</summary>
    public const int STEP_UP_PIXELS = 4;

    // ---- running / falling ----
    public const float MAX_RUN_SPEED = 320;      // px/s
    public const float GROUND_ACCEL = 2400;     // px/s²  (~0.13 s from stand to max)
    public const float GROUND_FRICTION = 1800;  // px/s²  when no input held
    public const float AIR_ACCEL = 1200;        // px/s²  weaker air control
    public const float GRAVITY = 1500;         // px/s²
    public const float MAX_FALL_SPEED = 900;     // px/s

    // ---- jumps ----
    // Jump budget: 2 per "grounding". A ground/coyote/wall jump spends the
    // first; air jumps spend the rest. Going airborne WITHOUT jumping (ledge
    // fall past coyote, rope release) leaves the full budget usable mid-air.
    public const int TOTAL_JUMPS = 2;
    public const float JUMP_SPEED = 550;        // px/s   ground/coyote jump
    public const float AIR_JUMP_SPEED = 500;     // px/s   slightly weaker
    public const float WALL_SLIDE_MAX_FALL = 160; // px/s   fall cap while pressing into a wall
    public const float WALL_JUMP_SPEED_Y = 520;   // px/s   upward
    public const float WALL_JUMP_KICK_X = 380;    // px/s   away from the wall

    // Coyote time scales with horizontal speed: fast exits off a ledge buy
    // more grace, rewarding momentum play.
    public const float COYOTE_BASE = 0.067f;            // s     grace at a standstill
    public const float COYOTE_BONUS_PER_100_SPEED = 0.021f;// s     extra grace per 100 px/s of exit speed
    public const float COYOTE_MAX = 0.2f;               // s     cap

    // ---- dash ----
    public const float DASH_SPEED = 650;        // px/s   8-way impulse along held keys
    public const float DASH_COOLDOWN = 0.67f;   // s

    // ---- rope ----
    public const float ROPE_SPEED = 1300;       // px/s   hook projectile
    public const float ROPE_MAX_RANGE = 520;     // px     hook flies this far, then fizzles
    public const float ROPE_REEL_SPEED = 300;    // px/s   constant pull while attached
    public const float ROPE_MIN_LENGTH = 24;     // px
    public const float ROPE_ATTACH_IMPULSE = 160;// px/s   the tug you feel on attach
    // Re-fire cooldowns: cheap after a productive rope, punishing after a whiff.
    public const float ROPE_RELEASE_COOLDOWN = 0.25f; // s  after releasing an attached rope
    public const float ROPE_MISS_COOLDOWN = 1.0f;     // s  after the hook hits nothing

    // ---- dev tools ----
    /// <summary>Radius of the dev click-to-carve, the stand-in weapon until mortars exist.</summary>
    public const int DEBUG_CARVE_RADIUS = 24;

    // ---- derived tick values (edit the seconds above, not these) ----
    public const int DASH_COOLDOWN_TICKS = (int)(DASH_COOLDOWN * TICK_RATE);
    public const int COYOTE_BASE_TICKS = (int)(COYOTE_BASE * TICK_RATE);
    public const int COYOTE_MAX_TICKS = (int)(COYOTE_MAX * TICK_RATE);
    public const int ROPE_RELEASE_COOLDOWN_TICKS = (int)(ROPE_RELEASE_COOLDOWN * TICK_RATE);
    public const int ROPE_MISS_COOLDOWN_TICKS = (int)(ROPE_MISS_COOLDOWN * TICK_RATE);
}
