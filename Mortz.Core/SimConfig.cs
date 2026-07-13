namespace Mortz.Core;

/// <summary>
/// Simulation constants. Units are pixels and seconds; +Y is down (screen space),
/// matching Godot 2D so positions pass through the shell unconverted.
/// Game feel is tuned here, but these are only the defaults: hosts override
/// the tweakable ones through MatchConfig, and the sim reads that (or the
/// player's PlayerStats) instead. The sim only reads this class directly for
/// infrastructure that must not vary per match: tick rate, hitbox, skin
/// sheet, numerical guards.
/// Durations are in seconds; the *Ticks values are derived at compile time,
/// don't edit those. Arena dimensions come from the loaded map.
/// </summary>
public static class SimConfig
{
    public const int TICK_RATE = 60;
    public const float DT = 1f / TICK_RATE;

    public const float PLAYER_HALF_WIDTH = 16;
    public const float PLAYER_HALF_HEIGHT = 16;

    /// <summary>Critter sprites on the sheet; the server deals one per player at join.</summary>
    public const int SKIN_COUNT = 25;

    /// <summary>Max wall height (px) walked over automatically; makes carved rubble traversable.</summary>
    public const int STEP_UP_PIXELS = 4;

    // ---- running / falling ----
    public const float MAX_RUN_SPEED = 320;      // px/s
    public const float GROUND_ACCEL = 2400;     // px/s^2  (~0.13 s from stand to max)
    public const float GROUND_FRICTION = 1800;  // px/s^2  when no input held
    public const float AIR_ACCEL = 1200;        // px/s^2  weaker air control
    public const float GRAVITY = 1500;         // px/s^2
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
    public const float ROPE_PULL_ACCEL = 2500;   // px/s^2  toward the anchor while taut; swings build speed
    public const float ROPE_SHORTEN_SPEED = 150; // px/s   slow rest-length creep; this is the climb
    public const float ROPE_MIN_LENGTH = 24;     // px
    // Re-fire cooldowns: cheap after a productive rope, punishing after a whiff.
    public const float ROPE_RELEASE_COOLDOWN = 0.25f; // s  after releasing an attached rope
    public const float ROPE_MISS_COOLDOWN = 1.0f;     // s  after the hook hits nothing

    // ---- mortar ----
    public const float MORTAR_SPEED = 900;        // px/s   muzzle speed along the aim
    public const float MORTAR_INHERIT = 0.5f;     //        fraction of the shooter's velocity added to the shot
    public const float MORTAR_GRAVITY = 900;      // px/s^2  floatier than players, for longer arcs
    public const float MORTAR_MAX_FALL = 900;     // px/s
    public const float MORTAR_MUZZLE_OFFSET = 20; // px     spawn distance from body center along the aim
    /// <summary>Explosion radius: the carved hole and the kill zone (LieroX-sized).</summary>
    public const int MORTAR_CARVE_RADIUS = 48;    // px
    // No cooldown between shots: the magazine is the limiter. Reload loads one
    // shell per second until full (auto when empty, R anytime below full) and
    // each completed second banks a shell right away. Firing mid-reload is
    // allowed if a shell is loaded, but scraps the one in progress and stops
    // the reload; banked shells are kept.
    public const int MORTAR_MAX_AMMO = 5;
    public const float MORTAR_RELOAD_PER_SHELL = 1.0f; // s

    // ---- parry ----
    // F raises a bubble that flips any incoming shell straight back along its
    // path. The cooldown is charged in full at the press; a successful deflect
    // refunds it, so good parries chain, only a whiff pays.
    public const float PARRY_RADIUS = 40;     // px    around the body center
    public const float PARRY_WINDOW = 0.5f;   // s     bubble uptime per press
    public const float PARRY_COOLDOWN = 20f;  // s     the price of a whiff

    // ---- health / blast ----
    // The blast circle is MORTAR_CARVE_RADIUS. Inside the core fraction a hit
    // is a guaranteed kill; from there damage falls off linearly to the rim.
    // Distances are measured to the nearest point of the body box.
    public const int MAX_HEALTH = 100;
    public const int MORTAR_DAMAGE = 100;           //        in the lethal core
    public const float BLAST_CORE_FRACTION = 0.75f; //        of the radius
    public const int BLAST_EDGE_DAMAGE = 35;        //        at the rim
    /// <summary>How long a gibbed body stays dead (and hidden) before respawning.</summary>
    public const float RESPAWN_DELAY = 2.0f;        // s

    // ---- mode ----
    /// <summary>Default first-to-X for the kills win conditions.</summary>
    public const int KILL_TARGET = 20;

    // ---- dev tools ----
    /// <summary>Radius of the dev click-to-carve, the stand-in weapon until mortars exist.</summary>
    public const int DEBUG_CARVE_RADIUS = 24;

    // ---- derived tick values (edit the seconds above, not these) ----
    public const int DASH_COOLDOWN_TICKS = (int)(DASH_COOLDOWN * TICK_RATE);
    public const int COYOTE_BASE_TICKS = (int)(COYOTE_BASE * TICK_RATE);
    public const int COYOTE_MAX_TICKS = (int)(COYOTE_MAX * TICK_RATE);
    public const int ROPE_RELEASE_COOLDOWN_TICKS = (int)(ROPE_RELEASE_COOLDOWN * TICK_RATE);
    public const int ROPE_MISS_COOLDOWN_TICKS = (int)(ROPE_MISS_COOLDOWN * TICK_RATE);
    public const int MORTAR_RELOAD_TICKS = (int)(MORTAR_RELOAD_PER_SHELL * TICK_RATE);
    public const int RESPAWN_DELAY_TICKS = (int)(RESPAWN_DELAY * TICK_RATE);
    public const int PARRY_WINDOW_TICKS = (int)(PARRY_WINDOW * TICK_RATE);
    public const int PARRY_COOLDOWN_TICKS = (int)(PARRY_COOLDOWN * TICK_RATE);
}
