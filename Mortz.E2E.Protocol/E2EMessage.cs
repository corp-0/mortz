using System.Text.Json.Serialization;

namespace Mortz.E2E.Protocol;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(ResponseMessage), "response")]
[JsonDerivedType(typeof(EventMessage), "event")]
public abstract record E2EMessage;
