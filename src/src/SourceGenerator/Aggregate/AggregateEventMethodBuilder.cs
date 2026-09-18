using System.Globalization;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Purview.EventSourcing.SourceGenerator.Aggregate;

static class AggregateEventMethodBuilder
{
	const int HintNameHashHexLength = 16;

	const string GeneratedSourceFileSuffix = ".g.cs";

	static readonly int HintNameSeparatorAndSuffixLength = 1 + HintNameHashHexLength + GeneratedSourceFileSuffix.Length;

	public static bool TryBuild(
		INamedTypeSymbol classSymbol,
		IMethodSymbol methodSymbol,
		Dictionary<string, IPropertySymbol> propertySymbolsByName,
		Compilation compilation,
		INamedTypeSymbol? valueObjectContextType,
		string? aggregateNamespace,
		string? aggregateEventNamespaceOverride,
		string? aggregateEventSuffixOverride,
		string? assemblyEventSuffix,
		ImmutableArray<ReportableDiagnostic>.Builder diagnostics,
		CancellationToken cancellationToken,
		out AggregateEventMethodInfo methodInfo
	)
	{
		methodInfo = default!;
		var eventSuffix = (aggregateEventSuffixOverride ?? assemblyEventSuffix ?? "Event").Trim();

		var eventAttribute = EventAttributeData.FromAttributeData(methodSymbol);
		var collectionEventAttribute = CollectionEventAttributeData.FromAttributeData(methodSymbol);
		var activeAttribute = eventAttribute.Exists || collectionEventAttribute.Exists;
		var version = collectionEventAttribute.Exists ? collectionEventAttribute.Version : eventAttribute.Version;
		var eventName = collectionEventAttribute.Exists ? collectionEventAttribute.EventName : eventAttribute.EventName;
		var eventNamespaceOverride = collectionEventAttribute.Exists
			? collectionEventAttribute.EventNamespace
			: eventAttribute.EventNamespace;
		var hasExplicitEventName = !string.IsNullOrWhiteSpace(eventName);

		if (eventAttribute.Exists && collectionEventAttribute.Exists)
		{
			diagnostics.Add(
				ReportableDiagnostic.Create(
					DiagnosticLibrary.UnsupportedEventMethodSignature,
					isBlocking: false,
					methodSymbol,
					methodSymbol.Name,
					$"methods cannot combine [{TypeLibrary.Purview.EventSourcing.Aggregates.EventAttribute.RenderTypeName}] and [{TypeLibrary.Purview.EventSourcing.Aggregates.CollectionEventAttribute.RenderTypeName}]"
				)
			);
			return false;
		}

		if (!methodSymbol.IsPartialDefinition)
		{
			diagnostics.Add(
				ReportableDiagnostic.Create(
					DiagnosticLibrary.EventMethodMustBePartial,
					isBlocking: false,
					methodSymbol,
					methodSymbol.Name
				)
			);

			return false;
		}

		if (
			!ValidateSignature(
				classSymbol,
				methodSymbol,
				collectionEventAttribute.Exists,
				diagnostics,
				out var returnTypeName,
				out var returnKind
			)
		)
			return false;

		if (version < 1)
		{
			diagnostics.Add(
				ReportableDiagnostic.Create(
					DiagnosticLibrary.EventSchemaVersionMustBePositive,
					isBlocking: false,
					methodSymbol,
					methodSymbol.Name,
					methodSymbol.ContainingType.Name,
					version
				)
			);

			return false;
		}

		var manualApply = collectionEventAttribute.Exists ? collectionEventAttribute.Manual : eventAttribute.Manual;

		if (
			!BuildParameters(
				methodSymbol,
				classSymbol,
				collectionEventAttribute,
				manualApply,
				propertySymbolsByName,
				compilation,
				valueObjectContextType,
				diagnostics,
				cancellationToken,
				out var allParameters,
				out var eventParameters,
				out var computedParameters,
				out var nonComputedParameters,
				out var aggregateParameters,
				out var collectionEvent
			)
		)
			return false;

		if (
			!ResolveEventName(
				methodSymbol,
				classSymbol,
				collectionEventAttribute.Exists,
				collectionEvent,
				eventSuffix,
				hasExplicitEventName,
				eventName ?? string.Empty,
				diagnostics,
				out var resolvedEventName
			)
		)
			return false;

		var eventNamespace = string.IsNullOrWhiteSpace(eventNamespaceOverride)
			? string.IsNullOrWhiteSpace(aggregateEventNamespaceOverride)
				? CreateDefaultEventNamespace(aggregateNamespace, classSymbol.Name)
				: aggregateEventNamespaceOverride!.Trim()
			: eventNamespaceOverride!.Trim();

		var eventTypeFullName = string.IsNullOrWhiteSpace(eventNamespace)
			? $"global::{resolvedEventName}"
			: $"global::{eventNamespace}.{resolvedEventName}";

		TypeIdentity eventType = new(resolvedEventName, eventNamespace);

		var (userApplyMethodKind, userApplyMethodAccessibility) = DetectUserApplyMethodKind(
			classSymbol,
			compilation,
			eventType,
			cancellationToken
		);

		if (!TryCreateInvalidMethodStub(methodSymbol, [], out var invalidMethod, cancellationToken))
		{
			invalidMethod = new InvalidAggregateEventMethodInfo(
				$"{returnTypeName} {methodSymbol.Name}({string.Join(", ", methodSymbol.Parameters.Select(static p => p.Type))})",
				[]
			);
		}

		methodInfo = new(
			methodSymbol.Name,
			new(new TypeIdentity(resolvedEventName, eventNamespace)),
			allParameters,
			eventParameters,
			computedParameters,
			nonComputedParameters,
			aggregateParameters,
			returnTypeName,
			returnKind,
			methodSymbol.DeclaredAccessibility,
			version,
			manualApply,
			userApplyMethodKind,
			userApplyMethodAccessibility,
			collectionEvent,
			invalidMethod.Signature
		);

		return true;
	}

	static bool ValidateSignature(
		INamedTypeSymbol classSymbol,
		IMethodSymbol methodSymbol,
		bool isCollectionEvent,
		ImmutableArray<ReportableDiagnostic>.Builder diagnostics,
		out TypeReference returnType,
		out EventMethodReturnKind returnKind
	)
	{
		returnType = PurviewTypeLibrary.System.Void;
		returnKind = EventMethodReturnKind.Void;
		var hasErrors = false;

		void ReportUnsupportedSignature(string reason)
		{
			diagnostics.Add(
				ReportableDiagnostic.Create(
					DiagnosticLibrary.UnsupportedEventMethodSignature,
					isBlocking: false,
					methodSymbol,
					methodSymbol.Name,
					reason
				)
			);
			hasErrors = true;
		}

		if (methodSymbol.DeclaredAccessibility == Accessibility.Public && !EventVerbMap.IsVerbPhrase(methodSymbol.Name))
		{
			diagnostics.Add(
				ReportableDiagnostic.Create(
					DiagnosticLibrary.AggregateMethodShouldBeVerbPhrase,
					isBlocking: false,
					methodSymbol,
					methodSymbol.Name
				)
			);
		}

		if (methodSymbol.IsStatic)
			ReportUnsupportedSignature("static methods are not supported");

		if (methodSymbol.TypeParameters.Length > 0)
			ReportUnsupportedSignature("generic methods are not supported");

		if (!TryResolveReturnKind(methodSymbol, classSymbol, out returnType, out returnKind))
		{
			ReportUnsupportedSignature("methods must return void, bool, or the containing aggregate type");
		}

		if (
			methodSymbol.PartialImplementationPart is { } implementationPart
			&& !GeneratedCodeHelpers.IsGenerated(implementationPart)
		)
			ReportUnsupportedSignature("methods must be partial declarations without a body");

		foreach (var parameter in methodSymbol.Parameters)
		{
			if (parameter.RefKind != RefKind.None)
				ReportUnsupportedSignature("ref, in, and out parameters are not supported");

			if (parameter.IsParams && (!isCollectionEvent || parameter.Type is not IArrayTypeSymbol))
				ReportUnsupportedSignature("params parameters are not supported");
		}

		if (isCollectionEvent && methodSymbol.Parameters.Length != 1)
			ReportUnsupportedSignature("collection event methods must have exactly one parameter");

		return !hasErrors;
	}

	static (UserApplyMethodKind ApplyKind, TypeDeclarationAccessibility? Accessibility) DetectUserApplyMethodKind(
		INamedTypeSymbol classSymbol,
		Compilation compilation,
		TypeIdentity eventType,
		CancellationToken cancellationToken
	)
	{
		// We can't use the symbols here as the event type
		// likely hasn't been generated yet, so we have to
		// rely on the syntax and semantic model to find the Apply method that matches the event type.
		foreach (var syntaxRef in classSymbol.DeclaringSyntaxReferences)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var syntaxTree = syntaxRef.SyntaxTree;
			if (IsGeneratedSyntaxTree(syntaxTree))
				continue;

			if (syntaxRef.GetSyntax(cancellationToken) is not ClassDeclarationSyntax classDecl)
				continue;

			var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
			foreach (var member in classDecl.Members)
			{
				if (member is not MethodDeclarationSyntax methodDecl || methodDecl.Identifier.Text != "Apply")
					continue;

				if (methodDecl.ParameterList.Parameters.Count != 1)
					continue;

				var parameterSyntax = methodDecl.ParameterList.Parameters[0];
				if (parameterSyntax.Type is null)
					continue;

				if (!IsParameterTypeMatch(parameterSyntax, semanticModel, eventType, cancellationToken))
					continue;

				var accessbility = methodDecl.GetDeclaredAccessibility()?.ToTypeDeclarationAccessibility();
				var isPartial = methodDecl.Modifiers.Any(SyntaxKind.PartialKeyword);
				var hasBody = methodDecl.Body is not null || methodDecl.ExpressionBody is not null;
				if (isPartial && hasBody)
				{
					// The user has provided a partial implementation of the Apply method for this event type,
					// which means we also need to collect the accessibility of the method so
					// we can generate a partial method with no body but the same accessibility.
					return (UserApplyMethodKind.PartialImplementation, accessbility);
				}

				if (!isPartial && hasBody)
					return (UserApplyMethodKind.NonPartial, accessbility);
			}
		}

		return (UserApplyMethodKind.None, null);
	}

	static bool IsParameterTypeMatch(
		ParameterSyntax parameterSyntax,
		SemanticModel semanticModel,
		TypeIdentity eventType,
		CancellationToken cancellationToken
	)
	{
		var parameterTypeSyntax = parameterSyntax.Type;
		if (parameterTypeSyntax is null)
			return false;

		var parameterType = semanticModel.GetTypeInfo(parameterTypeSyntax, cancellationToken).Type;
		if (parameterType is not null)
		{
			var fullyQualified = parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			if (fullyQualified == eventType.MetadataFullName)
				return true;

			if (parameterType.MetadataName == eventType.Name)
				return true;
		}

		var syntaxText = parameterTypeSyntax.ToString();
		if (syntaxText == eventType.Name)
			return true;

		if (syntaxText == eventType.MetadataFullName)
			return true;

		if (syntaxText.EndsWith("." + eventType.Name, StringComparison.Ordinal))
		{
			var prefix = syntaxText.Substring(0, syntaxText.Length - eventType.Name.Length - 1);
			if (string.IsNullOrEmpty(prefix) || prefix == "global")
				return true;
		}

		return false;
	}

	static bool IsGeneratedSyntaxTree(SyntaxTree syntaxTree)
	{
		var filePath = syntaxTree.FilePath;
		if (string.IsNullOrEmpty(filePath))
			return false;

		if (filePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
			return true;

		// Check for common intermediate build directories (e.g., obj) to avoid analyzing generated files.
		return filePath.Contains("\\obj\\", StringComparison.Ordinal)
			|| filePath.Contains("/obj/", StringComparison.Ordinal);
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Purview.SourceGeneratorFramework",
		"PSGFR16:Prefer the nullable-context overload",
		Justification = "We work it out from the compilation"
	)]
	internal static TypeReference CreateTypeReference(ITypeSymbol typeSymbol, Compilation compilation)
	{
		if (
			typeSymbol is INamedTypeSymbol namedType
			&& namedType.IsGenericType
			&& namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
		)
		{
			return TypeReference.Create(namedType.TypeArguments[0]).Nullable(compilation);
		}

		// If the type is a value type, we can return it as is. If it's a reference type, we should mark it as nullable.
		return TypeReference.Create(typeSymbol);
	}

	static bool BuildParameters(
		IMethodSymbol methodSymbol,
		INamedTypeSymbol classSymbol,
		CollectionEventAttributeData collectionEventAttribute,
		bool manualApply,
		Dictionary<string, IPropertySymbol> propertySymbolsByName,
		Compilation compilation,
		INamedTypeSymbol? valueObjectContextType,
		ImmutableArray<ReportableDiagnostic>.Builder diagnostics,
		CancellationToken cancellationToken,
		out ImmutableArray<EventPropertyInfo> allParameters,
		out ImmutableArray<EventPropertyInfo> eventParameters,
		out ImmutableArray<EventPropertyInfo> computedParameters,
		out ImmutableArray<EventPropertyInfo> nonComputedParameters,
		out ImmutableArray<EventPropertyInfo> aggregateParameters,
		out CollectionEventInfo? collectionEvent
	)
	{
		collectionEvent = null;

		var allParametersBuilder = ImmutableArray.CreateBuilder<EventPropertyInfo>();
		var eventParametersBuilder = ImmutableArray.CreateBuilder<EventPropertyInfo>();
		var computedParametersBuilder = ImmutableArray.CreateBuilder<EventPropertyInfo>();
		var nonComputedParametersBuilder = ImmutableArray.CreateBuilder<EventPropertyInfo>();
		var aggregateParametersBuilder = ImmutableArray.CreateBuilder<EventPropertyInfo>();

		if (collectionEventAttribute.Exists)
		{
			if (
				!TryCreateCollectionEventInfo(
					methodSymbol,
					collectionEventAttribute,
					propertySymbolsByName,
					diagnostics,
					compilation,
					out var collectionParameter,
					out collectionEvent
				)
			)
			{
				return false;
			}

			AddParameter(collectionParameter);

			allParameters = allParametersBuilder.ToImmutable();
			eventParameters = eventParametersBuilder.ToImmutable();
			computedParameters = computedParametersBuilder.ToImmutable();
			nonComputedParameters = nonComputedParametersBuilder.ToImmutable();
			aggregateParameters = aggregateParametersBuilder.ToImmutable();

			return true;
		}

		var hasErrors = false;
		foreach (var parameter in methodSymbol.Parameters)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (
				TryBuildEventPropertyInfo(
					parameter,
					methodSymbol,
					classSymbol,
					manualApply,
					propertySymbolsByName,
					compilation,
					valueObjectContextType,
					diagnostics,
					out var propertyInfo
				)
			)
			{
				AddParameter(propertyInfo);
			}
			else
			{
				hasErrors = true;
			}
		}

		allParameters = allParametersBuilder.ToImmutable();
		eventParameters = eventParametersBuilder.ToImmutable();
		computedParameters = computedParametersBuilder.ToImmutable();
		nonComputedParameters = nonComputedParametersBuilder.ToImmutable();
		aggregateParameters = aggregateParametersBuilder.ToImmutable();

		return !hasErrors;

		void AddParameter(EventPropertyInfo propertyInfo)
		{
			allParametersBuilder.Add(propertyInfo);
			if (propertyInfo.IncludeInEvent)
				eventParametersBuilder.Add(propertyInfo);
			if (propertyInfo.IsComputed)
				computedParametersBuilder.Add(propertyInfo);
			else
				nonComputedParametersBuilder.Add(propertyInfo);

			if (propertyInfo.HasAggregateProperty)
				aggregateParametersBuilder.Add(propertyInfo);
		}
	}

	static bool TryBuildEventPropertyInfo(
		IParameterSymbol parameter,
		IMethodSymbol methodSymbol,
		INamedTypeSymbol classSymbol,
		bool manualApply,
		Dictionary<string, IPropertySymbol> propertySymbolsByName,
		Compilation compilation,
		INamedTypeSymbol? valueObjectContextType,
		ImmutableArray<ReportableDiagnostic>.Builder diagnostics,
		out EventPropertyInfo propertyInfo
	)
	{
		propertyInfo = default!;

		var aggregatePropertyName =
			GetAggregatePropertyNameOverride(parameter) ?? EventPropertyInfo.ToPropertyName(parameter.Name);
		var isComputedParameter = HasComputedAttribute(parameter);
		var isNotNull = HasNotNullAttribute(parameter);
		var isRequired = HasRequiredAttribute(parameter);
		var isStringParameter = parameter.Type.SpecialType == SpecialType.System_String;
		var parameterType = CreateTypeReference(parameter.Type, compilation);

		var metadata = MetadataAttributeData.FromAttributeData(parameter);

		if (metadata.Exists)
		{
			if (isComputedParameter)
			{
				diagnostics.Add(
					ReportableDiagnostic.Create(
						DiagnosticLibrary.EventParameterMustMapToWritableProperty,
						isBlocking: false,
						parameter,
						parameter.Name,
						methodSymbol.Name,
						classSymbol.Name,
						"parameter cannot be marked with both [Metadata] and [Computed]"
					)
				);
				return false;
			}

			propertyInfo = new EventPropertyInfo(
				parameter.Name,
				parameterType,
				parameterType,
				aggregatePropertyName,
				false,
				metadata.Store,
				parameterType,
				isStringParameter,
				EventParameterConversionKind.None,
				IsComputed: false,
				IsNotNull: isNotNull,
				IsRequired: isRequired,
				IsString: isStringParameter
			);

			return true;
		}

		if (
			manualApply
			&& (
				!propertySymbolsByName.TryGetValue(aggregatePropertyName, out var manualMappedProperty)
				|| manualMappedProperty.SetMethod is null
				|| manualMappedProperty.SetMethod.IsInitOnly
			)
		)
		{
			propertyInfo = new EventPropertyInfo(
				parameter.Name,
				parameterType,
				parameterType,
				aggregatePropertyName,
				false,
				true,
				parameterType,
				isStringParameter,
				EventParameterConversionKind.None,
				IsComputed: isComputedParameter,
				IsNotNull: isNotNull,
				IsRequired: isRequired,
				IsString: isStringParameter
			);
			return true;
		}

		if (!propertySymbolsByName.TryGetValue(aggregatePropertyName, out var propertySymbol))
		{
			diagnostics.Add(
				ReportableDiagnostic.Create(
					DiagnosticLibrary.EventParameterMustMapToWritableProperty,
					isBlocking: false,
					parameter,
					parameter.Name,
					methodSymbol.Name,
					classSymbol.Name,
					$"property '{aggregatePropertyName}' does not exist"
				)
			);
			return false;
		}

		if (propertySymbol.SetMethod is null)
		{
			diagnostics.Add(
				ReportableDiagnostic.Create(
					DiagnosticLibrary.EventParameterMustMapToWritableProperty,
					isBlocking: false,
					parameter,
					parameter.Name,
					methodSymbol.Name,
					classSymbol.Name,
					$"property '{aggregatePropertyName}' does not have a setter"
				)
			);
			return false;
		}

		if (propertySymbol.SetMethod.IsInitOnly)
		{
			diagnostics.Add(
				ReportableDiagnostic.Create(
					DiagnosticLibrary.EventParameterMustMapToWritableProperty,
					isBlocking: false,
					parameter,
					parameter.Name,
					methodSymbol.Name,
					classSymbol.Name,
					$"property '{aggregatePropertyName}' is init-only"
				)
			);
			return false;
		}

		var propertyType = CreateTypeReference(propertySymbol.Type, compilation);
		var conversionKind = ResolveParameterConversionKind(
			compilation,
			classSymbol,
			parameter.Type,
			propertySymbol.Type,
			valueObjectContextType
		);
		if (conversionKind is null)
		{
			diagnostics.Add(
				ReportableDiagnostic.Create(
					DiagnosticLibrary.EventParameterMustMapToWritableProperty,
					isBlocking: false,
					parameter,
					parameter.Name,
					methodSymbol.Name,
					classSymbol.Name,
					$"parameter type '{parameterType}' cannot be mapped to property '{aggregatePropertyName}' of type '{propertyType}' via implicit conversion or value-object Create(...)"
				)
			);
			return false;
		}

		if (
			conversionKind == EventParameterConversionKind.Implicit
			&& SymbolEqualityComparer.Default.Equals(parameter.Type, propertySymbol.Type)
			&& propertySymbol.Type.NullableAnnotation == NullableAnnotation.Annotated
			&& parameter.Type.NullableAnnotation != NullableAnnotation.Annotated
		)
		{
			diagnostics.Add(
				ReportableDiagnostic.Create(
					DiagnosticLibrary.EventParameterNullabilityMismatch,
					isBlocking: false,
					parameter,
					parameter.Name,
					methodSymbol.Name,
					aggregatePropertyName,
					propertyType
				)
			);
		}

		propertyInfo = new EventPropertyInfo(
			parameter.Name,
			parameterType,
			propertyType,
			propertySymbol.Name,
			true,
			true,
			propertyType,
			parameter.Type.SpecialType == SpecialType.System_String
				&& propertySymbol.Type.SpecialType == SpecialType.System_String,
			conversionKind.Value,
			IsComputed: isComputedParameter,
			IsNotNull: isNotNull,
			IsRequired: isRequired,
			IsString: isStringParameter
		);
		return true;
	}

	static bool ResolveEventName(
		IMethodSymbol methodSymbol,
		INamedTypeSymbol classSymbol,
		bool isCollectionEvent,
		CollectionEventInfo? collectionEvent,
		string eventSuffix,
		bool hasExplicitEventName,
		string eventName,
		ImmutableArray<ReportableDiagnostic>.Builder diagnostics,
		out string resolvedEventName
	)
	{
		resolvedEventName = eventName;

		if (hasExplicitEventName)
		{
			if (!EventVerbMap.IsPastTenseEventName(resolvedEventName))
			{
				diagnostics.Add(
					ReportableDiagnostic.Create(
						DiagnosticLibrary.EventNameOverrideShouldBePastTense,
						isBlocking: false,
						methodSymbol,
						resolvedEventName,
						methodSymbol.Name
					)
				);
			}
		}
		else if (!EventVerbMap.TryCreateGeneratedEventName(methodSymbol.Name, classSymbol.Name, out resolvedEventName))
		{
			var suggestedMethodName = EventVerbMap.TrySuggestVerbPhrase(methodSymbol.Name, out var suggestedVerbPhrase)
				? suggestedVerbPhrase
				: $"Create{TrimAggregateSuffix(classSymbol.Name)}";

			diagnostics.Add(
				ReportableDiagnostic.Create(
					DiagnosticLibrary.UnableToInferEventName,
					isBlocking: false,
					methodSymbol,
					methodSymbol.Name,
					suggestedMethodName
				)
			);
			return false;
		}
		else
		{
			if (
				isCollectionEvent
				&& collectionEvent is not null
				&& collectionEvent.ParameterShape == CollectionParameterShape.Array
			)
				resolvedEventName += "Array";

			resolvedEventName += eventSuffix;
		}

		return true;
	}

	public static bool IsCollectionLikeType(ITypeSymbol typeSymbol)
	{
		if (typeSymbol is IArrayTypeSymbol)
			return true;

		if (typeSymbol.SpecialType == SpecialType.System_String)
			return false;

		if (typeSymbol is not INamedTypeSymbol namedType)
			return false;

		if (
			namedType.IsGenericType
			&& namedType.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
		)
			return true;

		foreach (var interfaceSymbol in namedType.AllInterfaces)
		{
			if (
				interfaceSymbol is INamedTypeSymbol namedInterface
				&& namedInterface.IsGenericType
				&& namedInterface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
			)
				return true;
		}

		return false;
	}

	public static bool TryGetCollectionDetails(ITypeSymbol typeSymbol, out ITypeSymbol elementType, out bool isSet)
	{
		elementType = null!;
		isSet = false;

		if (typeSymbol is not INamedTypeSymbol namedType || !namedType.IsGenericType)
			return false;

		if (TypeLibrary.Purview.EventSourcing.EventStoreList.Equals(namedType.OriginalDefinition))
		{
			elementType = namedType.TypeArguments[0];
			isSet = false;
			return true;
		}

		if (TypeLibrary.Purview.EventSourcing.EventStoreSet.Equals(namedType.OriginalDefinition))
		{
			elementType = namedType.TypeArguments[0];
			isSet = true;
			return true;
		}

		return false;
	}

	public static bool TryGetIEnumerableElementType(ITypeSymbol typeSymbol, out ITypeSymbol elementType)
	{
		elementType = null!;

		if (
			typeSymbol is INamedTypeSymbol namedType
			&& namedType.IsGenericType
			&& namedType.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
		)
		{
			elementType = namedType.TypeArguments[0];
			return true;
		}

		if (typeSymbol is not INamedTypeSymbol interfaceCarrier)
			return false;

		foreach (var interfaceSymbol in interfaceCarrier.AllInterfaces)
		{
			if (
				interfaceSymbol is INamedTypeSymbol enumerableInterface
				&& enumerableInterface.IsGenericType
				&& enumerableInterface.OriginalDefinition.SpecialType
					== SpecialType.System_Collections_Generic_IEnumerable_T
			)
			{
				elementType = enumerableInterface.TypeArguments[0];
				return true;
			}
		}

		return false;
	}

	public static bool IsEventStoreCollectionType(ITypeSymbol typeSymbol) =>
		typeSymbol is INamedTypeSymbol namedType
		&& namedType.IsGenericType
		&& (
			TypeLibrary.Purview.EventSourcing.EventStoreList.Equals(namedType.OriginalDefinition)
			|| TypeLibrary.Purview.EventSourcing.EventStoreSet.Equals(namedType.OriginalDefinition)
		);

	public static bool TryGetComplexScalarValueType(ITypeSymbol typeSymbol, out string valueTypeDisplayName)
	{
		valueTypeDisplayName = string.Empty;

		if (typeSymbol is not INamedTypeSymbol namedType)
			return false;

		if (!HasAttribute(namedType, TypeLibrary.Purview.ValueObjects.Serialization.ScalarAttribute))
			return false;

		var valueProperty = namedType
			.GetMembers("Value")
			.OfType<IPropertySymbol>()
			.FirstOrDefault(static property => property.GetMethod is not null && !property.IsStatic);

		if (valueProperty is null)
			return false;

		if (IsSimpleQueryScalarType(valueProperty.Type))
			return false;

		valueTypeDisplayName = valueProperty.Type.ToDisplayString(
			SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
				SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
					| SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
			)
		);
		return true;
	}

	public static bool IsSimpleQueryScalarType(ITypeSymbol typeSymbol)
	{
		if (typeSymbol.TypeKind == TypeKind.Enum)
			return true;

		if (typeSymbol.SpecialType is not SpecialType.None)
			return true;

		// Check for common system types that are often used as scalar values in queries.
		return TypeLibrary.System.Guid.Equals(typeSymbol)
			|| TypeLibrary.System.Uri.Equals(typeSymbol)
			|| PurviewTypeLibrary.System.DateTime.Equals(typeSymbol)
			|| PurviewTypeLibrary.System.DateTimeOffset.Equals(typeSymbol)
			|| PurviewTypeLibrary.System.TimeSpan.Equals(typeSymbol)
			|| PurviewTypeLibrary.System.DateOnly.Equals(typeSymbol)
			|| PurviewTypeLibrary.System.TimeOnly.Equals(typeSymbol);
	}

	public static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attributeSymbol)
	{
		if (attributeSymbol is null)
			return false;

		foreach (var attribute in symbol.GetAttributes())
		{
			if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol))
				return true;
		}

		return false;
	}

	public static bool HasAttribute(ISymbol symbol, TypeIdentity attributeType) =>
		TypeHelpers.HasAttribute(symbol, attributeType.MetadataFullName);

	public static bool HasComputedAttribute(IParameterSymbol parameterSymbol)
	{
		foreach (var attribute in parameterSymbol.GetAttributes())
		{
			var attributeClass = attribute.AttributeClass;
			if (
				attributeClass is not null
				&& TypeLibrary.Purview.EventSourcing.Aggregates.ComputedAttribute.Equals(attributeClass)
			)
				return true;
		}

		return false;
	}

	public static bool HasNotNullAttribute(IParameterSymbol parameterSymbol)
	{
		foreach (var attribute in parameterSymbol.GetAttributes())
		{
			var attributeClass = attribute.AttributeClass;
			if (
				attributeClass is not null
				&& attributeClass.ToDisplayString() == "System.Diagnostics.CodeAnalysis.NotNullAttribute"
			)
				return true;
		}

		return false;
	}

	public static bool HasRequiredAttribute(IParameterSymbol parameterSymbol)
	{
		foreach (var attribute in parameterSymbol.GetAttributes())
		{
			var attributeClass = attribute.AttributeClass;
			if (
				attributeClass is not null
				&& attributeClass.ToDisplayString() == "System.ComponentModel.DataAnnotations.RequiredAttribute"
			)
				return true;
		}

		return false;
	}

	public static bool IsEventType(INamedTypeSymbol typeSymbol) =>
		typeSymbol
			.GetAttributes()
			.Any(attribute =>
				attribute.AttributeClass is not null
				&& TypeLibrary.Purview.EventSourcing.Aggregates.Events.EventContractAttribute.Equals(
					attribute.AttributeClass
				)
			);

	public static string? GetAggregatePropertyNameOverride(IParameterSymbol parameterSymbol)
	{
		foreach (var attribute in parameterSymbol.GetAttributes())
		{
			var attributeClass = attribute.AttributeClass;
			if (
				attributeClass is null
				|| !TypeLibrary.Purview.EventSourcing.Aggregates.PropertyAttribute.Equals(attributeClass)
			)
				continue;

			if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string value)
				return value.Trim();

			break;
		}

		return null;
	}

	public static bool HasRegisterEventsMethod(INamedTypeSymbol classSymbol, out IMethodSymbol? registerEventsMethod)
	{
		registerEventsMethod = classSymbol
			.GetMembers("RegisterEvents")
			.OfType<IMethodSymbol>()
			.FirstOrDefault(method =>
				method.Parameters.Length == 0
				&& method.MethodKind == MethodKind.Ordinary
				&& !method.IsImplicitlyDeclared
				&& !GeneratedCodeHelpers.IsGenerated(method)
			);

		return registerEventsMethod is not null;
	}

	public static bool TryResolveReturnKind(
		IMethodSymbol methodSymbol,
		INamedTypeSymbol classSymbol,
		out TypeReference returnType,
		out EventMethodReturnKind returnKind
	)
	{
		returnType = PurviewTypeLibrary.System.Void;
		returnKind = EventMethodReturnKind.Void;

		if (methodSymbol.ReturnsVoid)
			return true;

		if (methodSymbol.ReturnType.SpecialType == SpecialType.System_Boolean)
		{
			returnType = PurviewTypeLibrary.System.Boolean;
			returnKind = EventMethodReturnKind.Bool;
			return true;
		}

		if (SymbolEqualityComparer.Default.Equals(methodSymbol.ReturnType, classSymbol))
		{
			returnType = TypeReference.Create(classSymbol);
			returnKind = EventMethodReturnKind.Aggregate;
			return true;
		}

		return false;
	}

	public static bool TryCreateInvalidMethodStub(
		IMethodSymbol methodSymbol,
		string[] diagnosticIds,
		out InvalidAggregateEventMethodInfo methodInfo,
		CancellationToken cancellationToken
	)
	{
		cancellationToken.ThrowIfCancellationRequested();
		methodInfo = null!;

		var declaration = methodSymbol
			.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax(cancellationToken))
			.OfType<MethodDeclarationSyntax>()
			.FirstOrDefault(static syntax =>
				syntax.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword))
				&& syntax.Body is null
				&& syntax.ExpressionBody is null
			);

		if (declaration is null)
			return false;

		var modifiers = string.Join(" ", declaration.Modifiers.Select(static modifier => modifier.Text));
		if (modifiers.Length > 0)
			modifiers += " ";

		var explicitInterfaceSpecifier = declaration.ExplicitInterfaceSpecifier?.ToString() ?? string.Empty;
		var typeParameterList = declaration.TypeParameterList?.ToString() ?? string.Empty;
		var constraints =
			declaration.ConstraintClauses.Count == 0
				? string.Empty
				: " " + string.Join(" ", declaration.ConstraintClauses.Select(static clause => clause.ToString()));

		var parameterList =
			declaration.ParameterList.Parameters.Count == 0
				? "()"
				: $"({string.Join(", ", declaration.ParameterList.Parameters.Select(static p =>
			{
				var parameterModifiers = p.Modifiers.ToString();
				if (parameterModifiers.Length > 0)
					parameterModifiers += " ";
				var defaultValue = p.Default?.ToString() ?? string.Empty;
				return $"{parameterModifiers}{p.Type} {p.Identifier}{defaultValue}";
			}))})";

		methodInfo = new InvalidAggregateEventMethodInfo(
			$"{modifiers}{declaration.ReturnType} {explicitInterfaceSpecifier}{declaration.Identifier}{typeParameterList}{parameterList}{constraints}",
			diagnosticIds.ToImmutableArray()
		);
		return true;
	}

	public static string CreateHintName(INamedTypeSymbol classSymbol)
	{
		var symbolName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		var shortName = classSymbol.Name;
		System.Text.StringBuilder builder = new(shortName.Length + HintNameSeparatorAndSuffixLength);

		foreach (var character in shortName)
		{
			builder.Append(char.IsLetterOrDigit(character) ? character : '_');
		}

		builder.Append('_');
		builder.Append(
			ComputeStableHash(symbolName).ToString($"X{HintNameHashHexLength}", CultureInfo.InvariantCulture)
		);
		builder.Append(GeneratedSourceFileSuffix);
		return builder.ToString();

		static ulong ComputeStableHash(string value)
		{
			const ulong offsetBasis = 14695981039346656037;
			const ulong prime = 1099511628211;

			var hash = offsetBasis;
			foreach (var character in value)
			{
				hash ^= character;
				hash *= prime;
			}

			return hash;
		}
	}

	static EventParameterConversionKind? ResolveParameterConversionKind(
		Compilation compilation,
		INamedTypeSymbol aggregateType,
		ITypeSymbol parameterType,
		ITypeSymbol propertyType,
		INamedTypeSymbol? valueObjectContextType
	)
	{
		if (SymbolEqualityComparer.Default.Equals(parameterType, propertyType))
		{
			// Same underlying type. If nullability differs (non-nullable param → nullable property),
			// return Implicit so a typed local variable is generated and ref-hook calls don't produce
			// CS8600 ("Converting null literal or possible null value to non-nullable type").
			return
				parameterType.NullableAnnotation != propertyType.NullableAnnotation
				&& propertyType.NullableAnnotation == NullableAnnotation.Annotated
				? EventParameterConversionKind.Implicit
				: EventParameterConversionKind.None;
		}

		if (
			propertyType is INamedTypeSymbol namedPropertyType
			&& TryResolveValueObjectCreateConversion(
				aggregateType,
				namedPropertyType,
				parameterType,
				valueObjectContextType,
				out var createConversionKind
			)
		)
		{
			return createConversionKind;
		}

		var conversion = compilation.ClassifyConversion(parameterType, propertyType);
		return conversion.Exists && conversion.IsImplicit ? EventParameterConversionKind.Implicit : null;
	}

	static bool TryResolveValueObjectCreateConversion(
		INamedTypeSymbol aggregateType,
		INamedTypeSymbol propertyType,
		ITypeSymbol parameterType,
		INamedTypeSymbol? contextTypeDefinition,
		out EventParameterConversionKind conversionKind
	)
	{
		conversionKind = EventParameterConversionKind.None;

		var hasScalarAttribute = HasAttribute(
			propertyType,
			TypeLibrary.Purview.ValueObjects.Serialization.ScalarAttribute
		);
		var createMethods = propertyType.GetMembers("Create").OfType<IMethodSymbol>().ToArray();

		var hasContextualCreate = createMethods.Any(method =>
			IsContextualCreateMethod(method, propertyType, aggregateType, parameterType, contextTypeDefinition)
		);

		if (hasContextualCreate)
		{
			conversionKind = EventParameterConversionKind.ContextualCreate;
			return true;
		}

		var hasSimpleCreate = createMethods.Any(method => IsSimpleCreateMethod(method, propertyType, parameterType));

		if (hasSimpleCreate || hasScalarAttribute)
		{
			conversionKind = EventParameterConversionKind.Create;
			return true;
		}

		return false;
	}

	static bool IsSimpleCreateMethod(IMethodSymbol method, ITypeSymbol returnType, ITypeSymbol parameterType)
	{
		return method.IsStatic
			&& method.DeclaredAccessibility == Accessibility.Public
			&& method.Name == "Create"
			&& method.Parameters.Length == 1
			&& SymbolEqualityComparer.Default.Equals(method.ReturnType, returnType)
			&& SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, parameterType);
	}

	static bool IsContextualCreateMethod(
		IMethodSymbol method,
		ITypeSymbol returnType,
		INamedTypeSymbol aggregateType,
		ITypeSymbol parameterType,
		INamedTypeSymbol? contextTypeDefinition
	)
	{
		if (!method.IsStatic || method.DeclaredAccessibility != Accessibility.Public || method.Name != "Create")
			return false;

		if (method.Parameters.Length != 2)
			return false;

		if (!SymbolEqualityComparer.Default.Equals(method.ReturnType, returnType))
			return false;

		if (!SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, parameterType))
			return false;

		var contextParameter = method.Parameters[1];
		return contextParameter.RefKind == RefKind.In
			&& contextTypeDefinition is not null
			&& contextParameter.Type is INamedTypeSymbol contextType
			&& SymbolEqualityComparer.Default.Equals(contextType.OriginalDefinition, contextTypeDefinition)
			&& contextType.TypeArguments.Length == 1
			&& SymbolEqualityComparer.Default.Equals(contextType.TypeArguments[0], aggregateType);
	}

	static string TrimAggregateSuffix(string aggregateClassName) =>
		aggregateClassName.EndsWith("Aggregate", StringComparison.Ordinal)
			? aggregateClassName.Substring(0, aggregateClassName.Length - "Aggregate".Length)
			: aggregateClassName;

	static string CreateDefaultEventNamespace(string? aggregateNamespace, string aggregateClassName)
	{
		var aggregateNameWithoutSuffix = aggregateClassName;
		if (aggregateClassName.EndsWith("Aggregate", StringComparison.Ordinal))
		{
			aggregateNameWithoutSuffix = aggregateClassName.Substring(
				0,
				aggregateClassName.Length - "Aggregate".Length
			);
		}

		if (string.IsNullOrEmpty(aggregateNameWithoutSuffix))
			aggregateNameWithoutSuffix = aggregateClassName;

		return string.IsNullOrEmpty(aggregateNamespace)
			? aggregateNameWithoutSuffix
			: $"{aggregateNamespace}.{aggregateNameWithoutSuffix}Events";
	}

	static bool TryCreateCollectionEventInfo(
		IMethodSymbol methodSymbol,
		CollectionEventAttributeData collectionEventAttribute,
		Dictionary<string, IPropertySymbol> propertySymbolsByName,
		ImmutableArray<ReportableDiagnostic>.Builder diagnostics,
		Compilation compilation,
		out EventPropertyInfo parameterInfo,
		out CollectionEventInfo? collectionEvent
	)
	{
		parameterInfo = default!;
		collectionEvent = null;

		var methodLocation = methodSymbol.Locations.FirstOrDefault();
		if (methodSymbol.Parameters.Length != 1)
			return false;

		if (string.IsNullOrWhiteSpace(collectionEventAttribute.PropertyName))
		{
			diagnostics.Add(
				ReportableDiagnostic.Create(
					DiagnosticLibrary.UnsupportedEventMethodSignature,
					isBlocking: false,
					methodLocation,
					methodSymbol.Name,
					"collection property name must be provided via [CollectionEvent(nameof(CollectionProperty))]"
				)
			);

			return false;
		}

		if (!propertySymbolsByName.TryGetValue(collectionEventAttribute.PropertyName, out var collectionProperty))
		{
			diagnostics.Add(
				ReportableDiagnostic.Create(
					DiagnosticLibrary.EventParameterMustMapToWritableProperty,
					isBlocking: false,
					methodLocation,
					methodSymbol.Parameters[0].Name,
					methodSymbol.Name,
					methodSymbol.ContainingType.Name,
					$"collection property '{collectionEventAttribute.PropertyName}' does not exist"
				)
			);

			return false;
		}

		if (!TryGetCollectionDetails(collectionProperty.Type, out var elementType, out var isSet))
		{
			diagnostics.Add(
				ReportableDiagnostic.Create(
					DiagnosticLibrary.EventParameterMustMapToWritableProperty,
					isBlocking: false,
					methodLocation,
					methodSymbol.Parameters[0].Name,
					methodSymbol.Name,
					methodSymbol.ContainingType.Name,
					$"collection property '{collectionEventAttribute.PropertyName}' must use {TypeLibrary.Purview.EventSourcing.EventStoreList.MakeGeneric("T")} or {TypeLibrary.Purview.EventSourcing.EventStoreSet.MakeGeneric("T")}"
				)
			);
			return false;
		}

		var parameter = methodSymbol.Parameters[0];

		TypeReference parameterType;
		TypeReference eventPropertyType;
		CollectionParameterShape parameterShape;
		if (SymbolEqualityComparer.Default.Equals(parameter.Type, elementType))
		{
			parameterShape = CollectionParameterShape.Single;
			parameterType = CreateTypeReference(elementType, compilation);
			eventPropertyType = parameterType;
		}
		else if (TypeHelpers.IsArray(parameter.Type))
		{
			parameterShape = CollectionParameterShape.Array;
			parameterType = CreateTypeReference(elementType, compilation).MakeArray();
			eventPropertyType = parameterType;
		}
		else if (TryGetIEnumerableElementType(parameter.Type, out var enumerableElementType))
		{
			if (!SymbolEqualityComparer.Default.Equals(enumerableElementType, elementType))
			{
				var actualElementTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				diagnostics.Add(
					ReportableDiagnostic.Create(
						DiagnosticLibrary.UnsupportedEventMethodSignature,
						isBlocking: false,
						parameter,
						methodSymbol.Name,
						$"collection item type '{parameter.Type}' does not match '{actualElementTypeName}'"
					)
				);
				return false;
			}

			parameterShape = CollectionParameterShape.Enumerable;
			parameterType = PurviewTypeLibrary.System.Collections.Generic.IEnumerable.MakeGeneric(
				new TypeIdentity(elementType)
			);
			eventPropertyType = CreateTypeReference(elementType, compilation).MakeArray();
		}
		else
		{
			var elementTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			diagnostics.Add(
				ReportableDiagnostic.Create(
					DiagnosticLibrary.UnsupportedEventMethodSignature,
					isBlocking: false,
					parameter.Locations.FirstOrDefault() ?? methodLocation,
					methodSymbol.Name,
					$"collection methods only support '{elementTypeName}', '{elementTypeName}[]', or IEnumerable<{elementTypeName}> parameters"
				)
			);
			return false;
		}

		parameterInfo = new EventPropertyInfo(
			parameter.Name,
			parameterType,
			eventPropertyType,
			collectionProperty.Name,
			HasAggregateProperty: false,
			IncludeInEvent: true,
			EqualityComparerTypeName: eventPropertyType,
			UseStringOrdinalComparison: false,
			ParameterConversionKind: EventParameterConversionKind.None,
			IsComputed: false,
			IsParams: parameter.IsParams
		);

		var operation = ReadCollectionOperationString(methodSymbol) switch
		{
			"Add" => CollectionMutationOperation.Add,
			"Remove" => CollectionMutationOperation.Remove,
			"Auto" => methodSymbol.Name.StartsWith("Remove", StringComparison.Ordinal)
			|| methodSymbol.Name.StartsWith("Delete", StringComparison.Ordinal)
				? CollectionMutationOperation.Remove
				: CollectionMutationOperation.Add,
			_ => CollectionMutationOperation.Add,
		};

		collectionEvent = new CollectionEventInfo(
			collectionProperty.Name,
			CreateTypeReference(elementType, compilation),
			CreateTypeReference(collectionProperty.Type, compilation),
			isSet,
			operation,
			parameterShape,
			methodSymbol.Name
		);

		return true;
	}

	static string ReadCollectionOperationString(IMethodSymbol methodSymbol)
	{
		var attribute = methodSymbol
			.GetAttributes()
			.FirstOrDefault(attribute =>
				TypeLibrary.Purview.EventSourcing.Aggregates.CollectionEventAttribute.Equals(attribute.AttributeClass)
			);
		if (attribute is null)
			return "Auto";

		var operationArg = attribute.NamedArguments.FirstOrDefault(argument => argument.Key == "Operation");
		if (operationArg.Key is null)
			return "Auto";

		var value = operationArg.Value;
		if (value.IsNull || value.Kind != TypedConstantKind.Enum || value.Type is not INamedTypeSymbol enumType)
			return "Auto";

		var intValue = Convert.ToInt32(value.Value, CultureInfo.InvariantCulture);
		foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
		{
			if (
				member.HasConstantValue
				&& Convert.ToInt32(member.ConstantValue, CultureInfo.InvariantCulture) == intValue
			)
				return member.Name;
		}

		return "Auto";
	}
}
