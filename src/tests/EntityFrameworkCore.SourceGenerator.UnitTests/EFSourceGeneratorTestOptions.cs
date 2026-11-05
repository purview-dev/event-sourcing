using Purview.EventSourcing.EntityFrameworkCore.SourceGenerator.Helpers;

namespace Purview.EventSourcing.EntityFrameworkCore.SourceGenerator;

public sealed record EFSourceGeneratorTestOptions : SourceGeneratorTestOptions
{
	public EFSourceGeneratorTestOptions()
	{
		AdditionalNamespaces = ["System.Collections.Generic", "Purview.EventSourcing.Aggregates"];
		ExcludeGeneratedSourceHintNames = [TypeLibrary.EFOpaqueAttribute];
		AdditionalSources = [AggregateAttribute()];
	}

	static string AggregateAttribute()
	{
		return """
using System;

namespace Purview.EventSourcing.Aggregates;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
sealed class AggregateAttribute : Attribute;
""";
	}
}
