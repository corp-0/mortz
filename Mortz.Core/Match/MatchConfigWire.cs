namespace Mortz.Core.Match;

/// <summary>Binary layout for the match rules that ride inside WelcomeMsg.
/// Field order lives here, clamping lives in MatchConfig.</summary>
internal static class MatchConfigWire
{
    public static byte[] Serialize(MatchConfig config)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        Write(writer, config);
        return stream.ToArray();
    }

    public static MatchConfig Deserialize(byte[] data)
    {
        using MemoryStream stream = new(data, writable: false);
        using BinaryReader reader = new(stream);
        MatchConfig config = Read(reader);
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Trailing bytes in match configuration.");
        config.Clamp();
        return config;
    }

    private static void Write(BinaryWriter w, MatchConfig c)
    {
        w.Write(c.MaxRunSpeed);
        w.Write(c.GroundAccel);
        w.Write(c.GroundFriction);
        w.Write(c.AirAccel);
        w.Write(c.Gravity);
        w.Write(c.MaxFallSpeed);
        w.Write(c.TotalJumps);
        w.Write(c.JumpSpeed);
        w.Write(c.AirJumpSpeed);
        w.Write(c.WallSlideMaxFall);
        w.Write(c.WallJumpSpeedY);
        w.Write(c.WallJumpKickX);
        w.Write(c.CoyoteBase);
        w.Write(c.CoyoteBonusPer100Speed);
        w.Write(c.CoyoteMax);
        w.Write(c.DashSpeed);
        w.Write(c.DashCooldown);
        w.Write(c.RopeSpeed);
        w.Write(c.RopeMaxRange);
        w.Write(c.RopePullAccel);
        w.Write(c.RopeShortenSpeed);
        w.Write(c.RopeReleaseCooldown);
        w.Write(c.RopeMissCooldown);
        w.Write(c.MortarSpeed);
        w.Write(c.MortarInherit);
        w.Write(c.MortarGravity);
        w.Write(c.MortarMaxFall);
        w.Write(c.MortarCarveRadius);
        w.Write(c.MortarMaxAmmo);
        w.Write(c.MortarReloadPerShell);
        w.Write(c.ParryRadius);
        w.Write(c.ParryWindow);
        w.Write(c.ParryCooldown);
        w.Write(c.MaxHealth);
        w.Write(c.MortarDamage);
        w.Write(c.BlastCoreFraction);
        w.Write(c.BlastEdgeDamage);
        w.Write(c.RespawnDelay);
        w.Write(c.SpawnImmunity);
        w.Write(c.Teams);
        w.Write((byte)c.WinCondition);
        w.Write(c.KillTarget);
        w.Write(c.FriendlyFire);
        w.Write(c.SuicidePenalty);
    }

    private static MatchConfig Read(BinaryReader r) => new()
    {
        MaxRunSpeed = r.ReadSingle(),
        GroundAccel = r.ReadSingle(),
        GroundFriction = r.ReadSingle(),
        AirAccel = r.ReadSingle(),
        Gravity = r.ReadSingle(),
        MaxFallSpeed = r.ReadSingle(),
        TotalJumps = r.ReadInt32(),
        JumpSpeed = r.ReadSingle(),
        AirJumpSpeed = r.ReadSingle(),
        WallSlideMaxFall = r.ReadSingle(),
        WallJumpSpeedY = r.ReadSingle(),
        WallJumpKickX = r.ReadSingle(),
        CoyoteBase = r.ReadSingle(),
        CoyoteBonusPer100Speed = r.ReadSingle(),
        CoyoteMax = r.ReadSingle(),
        DashSpeed = r.ReadSingle(),
        DashCooldown = r.ReadSingle(),
        RopeSpeed = r.ReadSingle(),
        RopeMaxRange = r.ReadSingle(),
        RopePullAccel = r.ReadSingle(),
        RopeShortenSpeed = r.ReadSingle(),
        RopeReleaseCooldown = r.ReadSingle(),
        RopeMissCooldown = r.ReadSingle(),
        MortarSpeed = r.ReadSingle(),
        MortarInherit = r.ReadSingle(),
        MortarGravity = r.ReadSingle(),
        MortarMaxFall = r.ReadSingle(),
        MortarCarveRadius = r.ReadInt32(),
        MortarMaxAmmo = r.ReadInt32(),
        MortarReloadPerShell = r.ReadSingle(),
        ParryRadius = r.ReadSingle(),
        ParryWindow = r.ReadSingle(),
        ParryCooldown = r.ReadSingle(),
        MaxHealth = r.ReadInt32(),
        MortarDamage = r.ReadInt32(),
        BlastCoreFraction = r.ReadSingle(),
        BlastEdgeDamage = r.ReadInt32(),
        RespawnDelay = r.ReadSingle(),
        SpawnImmunity = r.ReadSingle(),
        Teams = r.ReadBoolean(),
        WinCondition = (WinCondition)r.ReadByte(),
        KillTarget = r.ReadInt32(),
        FriendlyFire = r.ReadBoolean(),
        SuicidePenalty = r.ReadBoolean(),
    };
}
