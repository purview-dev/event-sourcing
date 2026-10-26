using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Purview.EventSourcing.EntityFrameworkCore.SourceGenerator.Helpers;

namespace Purview.EventSourcing.EntityFrameworkCore.SourceGenerator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EFSnapshotShapeAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => DiagnosticLibrary.SupportedDiagnostics;

	public override void Initialize(AnalysisContext context)
	{
		if (context is null)
			throw new ArgumentNullException(nameof(context));

		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(OnCompilationStart);
	}

	/// <summary>
	/// Resolves the marker attributes once per compilation so the per-symbol checks compare symbol
	/// identity instead of re-matching names, and a compilation that declares no aggregate skips the
	/// snapshot walk entirely.
	/// </summary>
	static void OnCompilationStart(CompilationStartAnalysisContext context)
	{
		if (!TypeHelpers.HasType(context.Compilation, TypeLibrary.AggregateAttribute))
			return;

		SymbolResolver.TryResolve(context.Compilation, TypeLibrary.EFOpaqueAttribute, out var opaqueAttribute);
		SnapshotShapeAnalysis analysis = new(opaqueAttribute);

		// The shape rules walk the graph reachable from each aggregate and report on the member that
		// owns the problem, which is routinely declared on a different type. A symbol action may only
		// report within the symbol it was given, so these rules stay compilation-end - which is what
		// their WellKnownDiagnosticTags.CompilationEnd tags declare.
		context.RegisterCompilationEndAction(analysis.AnalyzeSnapshotShapes);

		// A property read is executable behavior, so it is an operation rather than a syntax concern.
		context.RegisterOperationAction(analysis.AnalyzeOpaqueQueryUse, OperationKind.PropertyReference);
	}

	sealed class SnapshotShapeAnalysis(INamedTypeSymbol? opaqueAttribute)
	{
		public void AnalyzeSnapshotShapes(CompilationAnalysisContext context)
		{
			HashSet<ITypeSymbol> visited = new(SymbolEqualityComparer.Default);
			HashSet<ITypeSymbol> materializationVisited = new(SymbolEqualityComparer.Default);
			foreach (var type in GetSourceTypes(context.Compilation.Assembly.GlobalNamespace))
			{
				context.CancellationToken.ThrowIfCancellationRequested();

				if (!TypeHelpers.HasAttribute(type, TypeLibrary.AggregateAttribute))
					continue;

				AnalyzeType(type, context, visited);
				AnalyzeMaterializableType(type, context, materializationVisited);
			}
		}

		void AnalyzeType(ITypeSymbol type, CompilationAnalysisContext context, HashSet<ITypeSymbol> visited)
		{
			context.CancellationToken.ThrowIfCancellationRequested();

			type = UnwrapCollection(type);
			if (
				!visited.Add(type)
				|| !type.Locations.Any(static location => location.IsInSource)
				|| type.SpecialType != SpecialType.None
				|| type.TypeKind is TypeKind.Enum or TypeKind.TypeParameter
			)
			{
				return;
			}

			foreach (
				var property in type.GetMembers()
					.OfType<IPropertySymbol>()
					.Where(static property =>
						!property.IsStatic && property.DeclaredAccessibility == Accessibility.Public
					)
			)
			{
				if (IsDictionaryLike(property.Type))
				{
					if (!HasOpaqueAttribute(property))
					{
						context.ReportDiagnostic(
							Diagnostic.Create(
								DiagnosticLibrary.UnsupportedDictionary,
								property.Locations.FirstOrDefault(),
								property.Name,
								property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
							)
						);
					}

					continue;
				}

				if (!HasOpaqueAttribute(property))
					AnalyzeType(property.Type, context, visited);
			}
		}

		static ITypeSymbol UnwrapCollection(ITypeSymbol type)
		{
			if (type is IArrayTypeSymbol array)
				return array.ElementType;

			if (type is INamedTypeSymbol named)
			{
				var enumerable = named.AllInterfaces.FirstOrDefault(static item =>
					item.IsGenericType && TypeHelpers.IsCollectionLike(item.ConstructedFrom)
				);
				if (enumerable is not null)
					return enumerable.TypeArguments[0];
			}

			return type;
		}

		static IEnumerable<INamedTypeSymbol> GetSourceTypes(INamespaceSymbol namespaceSymbol)
		{
			foreach (var member in namespaceSymbol.GetMembers())
			{
				if (member is INamespaceSymbol childNamespace)
				{
					foreach (var type in GetSourceTypes(childNamespace))
						yield return type;
				}
				else if (member is INamedTypeSymbol type && type.Locations.Any(static location => location.IsInSource))
				{
					yield return type;
				}
			}
		}

		public void AnalyzeOpaqueQueryUse(OperationAnalysisContext context)
		{
			var reference = (IPropertyReferenceOperation)context.Operation;
			if (!HasOpaqueAttribute(reference.Property) || !IsInsideSnapshotQuery(reference))
				return;

			context.ReportDiagnostic(
				Diagnostic.Create(DiagnosticLibrary.OpaqueQuery, GetMemberLocation(reference), reference.Property.Name)
			);
		}

		/// <summary>
		/// Reports on the member name alone rather than the whole access expression, matching where a
		/// reader looks for the offending property.
		/// </summary>
		static Location GetMemberLocation(IOperation operation) =>
			operation.Syntax is MemberAccessExpressionSyntax access
				? access.Name.GetLocation()
				: operation.Syntax.GetLocation();

		static bool IsInsideSnapshotQuery(IOperation operation)
		{
			for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
			{
				if (
					parent is IInvocationOperation invocation
					&& invocation.TargetMethod.Name is "QueryAsync" or "FirstOrDefaultAsync" or "SingleOrDefaultAsync"
				)
				{
					return true;
				}
			}

			return false;
		}

		bool HasOpaqueAttribute(ISymbol symbol) =>
			opaqueAttribute is not null
			&& symbol
				.GetAttributes()
				.Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, opaqueAttribute));

		static bool IsDictionaryLike(ITypeSymbol type) =>
			type is INamedTypeSymbol named ? IsDictionaryType(named) : false;

		static bool IsDictionaryType(INamedTypeSymbol type) =>
			type.IsGenericType
				? TypeHelpers.Is(
					type,
					TypeLibrary.DictionaryKV,
					TypeLibrary.IDictionaryKV,
					TypeLibrary.IReadOnlyDictionaryKV
				)
				: false;

		// EF constructs every structurally mapped snapshot member. A member type is materializable when it exposes
		// either a parameterless constructor or a constructor whose parameters all bind to scalar members: EF cannot
		// bind complex or collection constructor parameters during JSON materialization.
		void AnalyzeMaterializableType(
			ITypeSymbol type,
			CompilationAnalysisContext context,
			HashSet<ITypeSymbol> visited
		)
		{
			context.CancellationToken.ThrowIfCancellationRequested();

			if (type is not INamedTypeSymbol named || !IsInspectableType(named) || !visited.Add(named))
				return;

			var mappedMembers = GetMappedMembers(named);

			if (!HasEfUsableConstructor(named, mappedMembers))
			{
				var location = named.Locations.FirstOrDefault(static candidate => candidate.IsInSource);
				if (location is not null)
				{
					context.ReportDiagnostic(
						Diagnostic.Create(
							DiagnosticLibrary.UnconstructibleSnapshotType,
							location,
							named.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
						)
					);
				}
			}

			foreach (var member in mappedMembers)
			{
				if (!IsMappedStructuralMember(member))
					continue;

				if (type.TypeKind == TypeKind.Struct && member.SetMethod is null)
				{
					var location = member.Locations.FirstOrDefault(static candidate => candidate.IsInSource);
					if (location is not null)
					{
						context.ReportDiagnostic(
							Diagnostic.Create(
								DiagnosticLibrary.NonWritableValueTypeMember,
								location,
								member.Name,
								named.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
							)
						);
					}
				}

				foreach (var relatedType in GetStructuralMemberTypes(member))
					AnalyzeMaterializableType(relatedType, context, visited);
			}
		}

		IPropertySymbol[] GetMappedMembers(INamedTypeSymbol type) =>
			[.. type.GetMembers().OfType<IPropertySymbol>().Where(property => IsMappedMember(type, property))];

		bool IsMappedMember(INamedTypeSymbol declaringType, IPropertySymbol property) =>
			!property.IsStatic
			&& !property.IsIndexer
			&& property.DeclaredAccessibility == Accessibility.Public
			&& property.GetMethod is not null
			&& !TypeHelpers.HasAttribute(property, TypeLibrary.JsonIgnoreAttribute)
			&& (property.SetMethod is not null || HasBindableConstructorParameter(declaringType, property));

		static bool HasBindableConstructorParameter(INamedTypeSymbol type, IPropertySymbol property) =>
			type.InstanceConstructors.Any(constructor =>
				constructor.Parameters.Any(parameter =>
					SymbolEqualityComparer.Default.Equals(parameter.Type, property.Type)
					&& string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase)
				)
			);

		bool HasEfUsableConstructor(INamedTypeSymbol type, IReadOnlyList<IPropertySymbol> mappedMembers)
		{
			var constructors = type.InstanceConstructors.Where(static constructor => !constructor.IsStatic).ToArray();

			// A value type that declares no constructors is materialized from its default value plus member assignment.
			if (
				type.TypeKind == TypeKind.Struct
				&& constructors.All(static constructor => constructor.IsImplicitlyDeclared)
			)
			{
				return true;
			}

			foreach (var constructor in constructors)
			{
				if (
					constructor.Parameters.Length == 0
					&& (type.TypeKind != TypeKind.Struct || !constructor.IsImplicitlyDeclared)
				)
				{
					return true;
				}

				if (
					constructor.Parameters.Length > 0
					&& constructor.Parameters.All(parameter => IsBindableScalarParameter(mappedMembers, parameter))
				)
				{
					return true;
				}
			}

			return false;
		}

		bool IsBindableScalarParameter(IReadOnlyList<IPropertySymbol> mappedMembers, IParameterSymbol parameter) =>
			mappedMembers.Any(member =>
				string.Equals(member.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)
				&& SymbolEqualityComparer.Default.Equals(member.Type, parameter.Type)
				&& !IsMappedStructuralMember(member)
			);

		bool IsMappedStructuralMember(IPropertySymbol property)
		{
			if (HasOpaqueAttribute(property))
				return false;

			var memberType = UnwrapNullable(property.Type);

			if (TypeHelpers.HasAttribute(memberType, TypeLibrary.ScalarAttribute))
				return false;

			if (IsDictionaryLike(memberType))
				return false;

			if (memberType is INamedTypeSymbol collection && IsEventStoreCollection(collection))
			{
				var elementType = collection.TypeArguments[0];

				// Struct value objects and scalars are persisted through JSON or primitive collection conversions.
				if (elementType.IsValueType && TypeHelpers.HasAttribute(elementType, TypeLibrary.ValueObjectAttribute))
					return false;

				if (TypeHelpers.HasAttribute(elementType, TypeLibrary.ScalarAttribute))
					return false;

				// EF materializes collection elements through JSON conversion, so we only need to validate that the element type is constructible and not a system type.
				return IsInspectableType(elementType);
			}

			if (ImplementsGenericEnumerable(memberType))
				return false;

			// EF materializes complex types through JSON conversion, so we only need to validate that the type is constructible and not a system type.
			return memberType is INamedTypeSymbol named && IsInspectableType(named);
		}

		static IEnumerable<ITypeSymbol> GetStructuralMemberTypes(IPropertySymbol property)
		{
			var memberType = UnwrapNullable(property.Type);

			if (memberType is INamedTypeSymbol collection && IsEventStoreCollection(collection))
			{
				yield return collection.TypeArguments[0];
				yield break;
			}

			yield return memberType;
		}

		static bool IsEventStoreCollection(INamedTypeSymbol type) =>
			type.IsGenericType
			&& type.TypeArguments.Length == 1
			&& TypeHelpers.Is(type, TypeLibrary.EventStoreList, TypeLibrary.EventStoreSet);

		static bool ImplementsGenericEnumerable(ITypeSymbol type) =>
			type is INamedTypeSymbol named
			&& named.AllInterfaces.Any(static candidate =>
				candidate.IsGenericType
				&& candidate.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
			);

		static bool IsInspectableType(ITypeSymbol type) =>
			type is INamedTypeSymbol named
			&& !named.IsStatic
			&& !named.IsAbstract
			&& named.SpecialType == SpecialType.None
			&& named.TypeKind is TypeKind.Class or TypeKind.Struct
			&& named.ContainingNamespace is not null
			&& !named.ContainingNamespace.ToDisplayString().StartsWith("System", StringComparison.Ordinal);

		static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
			type is INamedTypeSymbol { IsGenericType: true } nullable
			&& nullable.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T
				? nullable.TypeArguments[0]
				: type;
	}
}
