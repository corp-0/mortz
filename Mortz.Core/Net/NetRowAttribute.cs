namespace Mortz.Core.Net;

/// <summary>
/// Marks a readonly record struct as a wire row, so [NetMessage] fields can be
/// TRow[] instead of parallel scalar arrays. A row holds scalars, byte-enums and
/// nullable byte-enums only: no arrays, no nested rows.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class NetRowAttribute : Attribute;
