// Mirrors the attribute emitted by Purview.EventSourcing.EntityFrameworkCore.SourceGenerator, which is not
// referenced by this project. Entity Framework snapshot providers detect the marker by full type name, so the
// declaration must keep the generated namespace and type name.
namespace Purview.EventSourcing.EntityFrameworkCore;

[System.AttributeUsage(System.AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class EFOpaqueAttribute : System.Attribute;
