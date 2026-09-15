namespace Purview.EventSourcing.SourceGenerator.Aggregate;

partial class AggregateSourceEmitter
{
	static void GenerateEventClass(
		AggregateEmitContext outputContext,
		CodeWriter writer,
		AggregateEventMethodInfo method
	)
	{
		outputContext.Debug(
			$"Generating event class '{method.EventType}' for method '{method.MethodName}' with {method.EventParameters.Count} stored parameters and version {method.Version}."
		);

		TypeDeclarationOptions declaration = new(method.EventType.Identity.Name, TypeDeclarationAccessibility.Public)
		{
			Kind = TypeDeclarationKind.RecordClass,
			IsSealed = true,
			IsPartial = false,
			Attributes =
			[
				new AttributeDeclarationOptions(
					TypeLibrary.Purview.EventSourcing.Aggregates.Events.EventContractAttribute
				),
			],
		};

		writer.Type(
			declaration,
			bodyWriter =>
			{
				foreach (var prop in method.EventParameters)
				{
					var propertyType = prop.PropertyType;

					bodyWriter.Property(
						new(prop.PropertyName, propertyType, TypeDeclarationAccessibility.Public)
						{
							HasSetter = true,
							Initializer = "default!",
						}
					);
				}

				bodyWriter.Property(
					new(
						"Metadata",
						TypeLibrary.Purview.EventSourcing.Aggregates.Events.EventMetadata,
						TypeDeclarationAccessibility.Public
					)
					{
						HasSetter = true,
						Attributes =
						[
							new AttributeDeclarationOptions(
								TypeLibrary.System.Text.Json.Serialization.JsonIgnoreAttribute
							),
						],
					}
				);

				bodyWriter.Property(
					new("SchemaVersion", PurviewTypeLibrary.System.Int32, TypeDeclarationAccessibility.Public)
					{
						IsStatic = true,
						ExpressionBody = $"{method.Version}",
					}
				);

				bodyWriter.Method(
					new("GetHashCode", PurviewTypeLibrary.System.Int32, TypeDeclarationAccessibility.Public)
					{
						IsOverride = true,
					},
					methodBodyWriter =>
					{
						methodBodyWriter.Assignment("var hash", new ObjectCreationOptions(TypeLibrary.System.HashCode));
						methodBodyWriter.MethodCallOn("hash", "Add", "GetType().FullName");
						methodBodyWriter.MethodCallOn("hash", "Add", "Metadata");
						methodBodyWriter.MethodCallOn("hash", "Add", "SchemaVersion");
						foreach (var prop in method.EventParameters)
							methodBodyWriter.MethodCallOn("hash", "Add", prop.PropertyName);
						methodBodyWriter.Return("hash.ToHashCode()");
					}
				);
			}
		);
	}
}
