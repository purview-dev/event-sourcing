using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Purview.EventSourcing.EntityFrameworkCore.SourceGenerator.Heleprs;

static class DiagnosticLibrary
{
	public static readonly DiagnosticDescriptor UnsupportedDictionary = new(
		"EVENTSTOREEF001",
		"Dictionary-like property cannot be mapped as an EF complex property",
		"Property '{0}' has dictionary-like type '{1}', which cannot be structurally mapped in an EF snapshot. Mark it [EFOpaque] to persist it as non-queryable JSON, or replace it with a collection of '{0}Entry' complex objects containing key and value members.",
		"EntityFrameworkCore",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		customTags: WellKnownDiagnosticTags.CompilationEnd
	);

	public static readonly DiagnosticDescriptor OpaqueQuery = new(
		"EVENTSTOREEF002",
		"Opaque EF snapshot property cannot be queried",
		"Property '{0}' is marked [EFOpaque] and is persisted as opaque JSON; it cannot be referenced in an EF snapshot query expression. Add a separately mapped query property when filtering or ordering is required.",
		"EntityFrameworkCore",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor UnconstructibleSnapshotType = new(
		"EVENTSTOREEF003",
		"EF snapshot type cannot be constructed by EF",
		"Type '{0}' is mapped into an EF snapshot payload but exposes no constructor EF can use. EF cannot bind complex or collection constructor parameters during JSON materialization, so snapshot queries fail with 'No suitable constructor was found'. Add a parameterless constructor (Purview.ValueObjects generates one for [ValueObject] types) or declare only scalar constructor parameters.",
		"EntityFrameworkCore",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		customTags: WellKnownDiagnosticTags.CompilationEnd
	);

	public static readonly DiagnosticDescriptor NonWritableValueTypeMember = new(
		"EVENTSTOREEF004",
		"Value-type EF snapshot member cannot be assigned by EF",
		"Member '{0}' on value type '{1}' has no setter, so EF assigns it through its backing field. EF cannot rebuild that assignment while materializing value-type JSON members, and snapshot queries fail with 'Expression must be writeable'. Add an 'init' or 'set' accessor, or mark the member [JsonIgnore]/[EFOpaque] when it must not be part of the snapshot payload.",
		"EntityFrameworkCore",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		customTags: WellKnownDiagnosticTags.CompilationEnd
	);

	// Leave this at the bottom...!
	public static ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		[UnsupportedDictionary, OpaqueQuery, UnconstructibleSnapshotType, NonWritableValueTypeMember];
}
