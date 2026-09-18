using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Purview.EventSourcing.SourceGenerator.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EventStoreAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		[
			DiagnosticLibrary.ComputedParameterCannotBeSetByCaller,
			DiagnosticLibrary.EventNameShouldBePastTense,
			DiagnosticLibrary.NullableScalarEqualityNullComparisonShouldUsePatternMatching,
			DiagnosticLibrary.EventMethodRequiresAggregateAttribute,
			DiagnosticLibrary.EventTypeShouldBeMarkedWithEventContract,
		];

	public override void Initialize(AnalysisContext context)
	{
		if (context is null)
			throw new ArgumentNullException(nameof(context));

		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		RegisterComputedParameterDiagnostics(context);
		RegisterManualEventTypeDiagnostics(context);
		RegisterNullableScalarComparisonDiagnostics(context);
		RegisterOrphanEventMethodDiagnostics(context);
		RegisterEventContractDiagnostics(context);
	}

	static void RegisterEventContractDiagnostics(AnalysisContext context) =>
		context.RegisterOperationAction(
			static context =>
			{
				var invocation = (IInvocationOperation)context.Operation;
				var targetMethod = invocation.TargetMethod;

				if (targetMethod.Name is not ("Register" or "RegisterGenerated"))
					return;

				if (targetMethod.ContainingType.ToDisplayString() != "Purview.EventSourcing.Aggregates.AggregateBase")
					return;

				if (targetMethod.TypeArguments.Length != 1)
					return;

				var eventType = targetMethod.TypeArguments[0];
				if (
					TypeHelpers.HasAttribute(
						eventType,
						TypeLibrary.Purview.EventSourcing.Aggregates.Events.EventContractAttribute
					)
				)
					return;

				var location =
					eventType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation()
					?? invocation.Syntax.GetLocation();

				context.ReportDiagnostic(
					Diagnostic.Create(
						DiagnosticLibrary.EventTypeShouldBeMarkedWithEventContract,
						location,
						eventType.Name
					)
				);
			},
			OperationKind.Invocation
		);

	static void RegisterComputedParameterDiagnostics(AnalysisContext context) =>
		context.RegisterOperationAction(
			static context =>
			{
				var invocation = (IInvocationOperation)context.Operation;
				var targetMethod = invocation.TargetMethod;

				if (
					!TypeHelpers.HasAttribute(targetMethod, TypeLibrary.Purview.EventSourcing.Aggregates.EventAttribute)
					&& !TypeHelpers.HasAttribute(
						targetMethod,
						TypeLibrary.Purview.EventSourcing.Aggregates.CollectionEventAttribute
					)
				)
				{
					return;
				}

				var computedParameter = targetMethod.Parameters.FirstOrDefault(static parameter =>
					TypeHelpers.HasAttribute(parameter, TypeLibrary.Purview.EventSourcing.Aggregates.ComputedAttribute)
				);

				if (computedParameter is null)
					return;

				var passedArgument = invocation.Arguments.FirstOrDefault(argument =>
					!argument.IsImplicit && SymbolEqualityComparer.Default.Equals(argument.Parameter, computedParameter)
				);

				if (passedArgument is null)
					return;

				context.ReportDiagnostic(
					Diagnostic.Create(
						DiagnosticLibrary.ComputedParameterCannotBeSetByCaller,
						passedArgument.Syntax.GetLocation(),
						targetMethod.Name,
						computedParameter.Name
					)
				);
			},
			OperationKind.Invocation
		);

	static void RegisterNullableScalarComparisonDiagnostics(AnalysisContext context)
	{
		context.RegisterOperationAction(
			static context =>
			{
				var binaryOperation = (IBinaryOperation)context.Operation;
				if (binaryOperation.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
					return;

				IOperation? comparedOperand = null;
				if (IsNullLiteral(binaryOperation.LeftOperand))
					comparedOperand = binaryOperation.RightOperand;
				else if (IsNullLiteral(binaryOperation.RightOperand))
					comparedOperand = binaryOperation.LeftOperand;

				if (comparedOperand is null)
					return;

				var comparedType = comparedOperand.Type;
				if (comparedType is null || !IsScalarValueObject(comparedType))
					return;

				var replacement = binaryOperation.OperatorKind == BinaryOperatorKind.Equals ? "is null" : "is not null";

				context.ReportDiagnostic(
					Diagnostic.Create(
						DiagnosticLibrary.NullableScalarEqualityNullComparisonShouldUsePatternMatching,
						binaryOperation.Syntax.GetLocation(),
						binaryOperation.Syntax.ToString(),
						replacement
					)
				);
			},
			OperationKind.BinaryOperator
		);
	}

	static bool IsNullLiteral(IOperation operation) =>
		operation.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax literal
		&& literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullLiteralExpression);

	static void RegisterManualEventTypeDiagnostics(AnalysisContext context)
	{
		context.RegisterSymbolAction(
			static context =>
			{
				var typeSymbol = (INamedTypeSymbol)context.Symbol;

				if (
					!TypeHelpers.HasAttribute(
						typeSymbol,
						TypeLibrary.Purview.EventSourcing.Aggregates.Events.EventContractAttribute
					)
				)
					return;

				if (
					TypeHelpers.HasAttribute(
						typeSymbol,
						TypeLibrary.Purview.EventSourcing.Aggregates.SentinelEventAttribute
					)
				)
				{
					return;
				}

				if (EventVerbMap.IsPastTenseEventName(typeSymbol.Name))
					return;

				var location = typeSymbol.Locations.FirstOrDefault(static location => location.IsInSource);

				if (location is null)
					return;

				context.ReportDiagnostic(
					Diagnostic.Create(DiagnosticLibrary.EventNameShouldBePastTense, location, typeSymbol.Name)
				);
			},
			SymbolKind.NamedType
		);
	}

	static void RegisterOrphanEventMethodDiagnostics(AnalysisContext context)
	{
		context.RegisterSymbolAction(
			static context =>
			{
				var methodSymbol = (IMethodSymbol)context.Symbol;

				if (
					!TypeHelpers.HasAttribute(methodSymbol, TypeLibrary.Purview.EventSourcing.Aggregates.EventAttribute)
					&& !TypeHelpers.HasAttribute(
						methodSymbol,
						TypeLibrary.Purview.EventSourcing.Aggregates.CollectionEventAttribute
					)
				)
				{
					return;
				}

				if (
					TypeHelpers.HasAttribute(
						methodSymbol.ContainingType,
						TypeLibrary.Purview.EventSourcing.Aggregates.AggregateAttribute
					)
				)
					return;

				var location = methodSymbol.Locations.FirstOrDefault(static location => location.IsInSource);
				if (location is null)
					return;

				context.ReportDiagnostic(
					Diagnostic.Create(
						DiagnosticLibrary.EventMethodRequiresAggregateAttribute,
						location,
						methodSymbol.Name
					)
				);
			},
			SymbolKind.Method
		);
	}

	static bool IsScalarValueObject(ITypeSymbol type)
	{
		if (
			type is INamedTypeSymbol namedType
			&& namedType.IsGenericType
			&& namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
		)
		{
			type = namedType.TypeArguments[0];
		}

		return TypeHelpers.HasAttribute(type, TypeLibrary.Purview.ValueObjects.Serialization.ScalarAttribute);
	}
}
