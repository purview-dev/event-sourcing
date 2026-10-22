// Mirrors the attribute emitted by Purview.EventSourcing.EntityFrameworkCore.SourceGenerator, which is not
// referenced by this project. Entity Framework snapshot providers detect the marker by full type name, so the
// declaration must keep the generated namespace and type name.

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Purview.EventSourcing.EntityFrameworkCore;
#pragma warning restore IDE0130 // Namespace does not match folder structure

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class EFOpaqueAttribute : Attribute;
