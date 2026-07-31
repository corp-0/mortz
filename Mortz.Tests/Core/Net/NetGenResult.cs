using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Mortz.Tests.Core.Net;

/// <summary>Assembly is null when the generated code did not compile.</summary>
internal readonly record struct NetGenResult(
    ImmutableArray<Diagnostic> Diagnostics,
    string Sources,
    Assembly? Assembly);
