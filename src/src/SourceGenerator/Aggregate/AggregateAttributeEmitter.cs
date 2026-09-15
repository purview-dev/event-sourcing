using Microsoft.CodeAnalysis.Text;
using Purview.EventSourcing.SourceGenerator.Generators;

namespace Purview.EventSourcing.SourceGenerator.Aggregate;

static class AggregateAttributeEmitter
{
	public static IEnumerable<(string HintName, SourceText Source)> Emit()
	{
		var writer = CreateCodeWriter(TypeLibrary.Purview.EventSourcing.Aggregates.AggregateAttribute);
		AggregateAttribute(writer);
		SentinelEventAttribute(writer);
		AggregateDefaultsAttribute(writer);
		CollectionEventAttribute(writer);
		ComputedAttribute(writer);
		EventAttribute(writer);
		MetadataAttribute(writer);
		PropertyAttribute(writer);

		yield return ("AggregateAttributes.g.cs", writer);
	}

	static void AggregateAttribute(CodeWriter writer)
	{
		writer.XmlSummary(
			"Marks a partial class extending <c>AggregateBase</c> for source generation.",
			"The generator will create the <c>RegisterEvents()</c> override and",
			$"event classes based on methods decorated with <see cref=\"{TypeLibrary.Purview.EventSourcing.Aggregates.EventAttribute}\" />."
		);

		writer.AttributeClass(
			new(TypeLibrary.Purview.EventSourcing.Aggregates.AggregateAttribute) { IsSealed = true },
			AttributeTargets.Class,
			body =>
			{
				body.XmlSummary(
						"Overrides the generated event namespace for all event methods on this aggregate.",
						"When not set, namespaces default to:",
						"<c>{Aggregate-Type-Namespace}.{Aggregate-Name-Without-The-Aggregate-Suffix}Events</c>."
					)
					.Property(
						new("EventNamespace", PurviewTypeLibrary.System.String.MakeNullable(writer))
						{
							Accessibility = TypeDeclarationAccessibility.Public,
							HasSetter = true,
							IsInitOnly = true,
						}
					);

				body.XmlSummary(
						"Appends a suffix to generated event type names when no explicit",
						$"<see cref=\"{TypeLibrary.Purview.EventSourcing.Aggregates.EventAttribute}.EventName\"/> is provided.",
						$"Overrides <see cref=\"{TypeLibrary.Purview.EventSourcing.Aggregates.AggregateDefaultsAttribute}.EventSuffix\"/> for this aggregate.",
						"If not set, the generator falls back to the assembly default or <c>Event</c>."
					)
					.Property(
						new("EventSuffix", PurviewTypeLibrary.System.String.MakeNullable(writer))
						{
							Accessibility = TypeDeclarationAccessibility.Public,
							HasSetter = true,
							IsInitOnly = true,
						}
					);
			}
		);
	}

	static void SentinelEventAttribute(CodeWriter writer)
	{
		writer
			.XmlSummary(
				"Marks an event type as a sentinel (fallback) event that is intentionally exempt from the",
				$"past-tense naming convention enforced by <c>{DiagnosticLibrary.EventNameShouldBePastTense.Id}</c>."
			)
			.XmlRemarks(
				CodeWriter.XmlInlinePara(
					"Domain events are named as past-tense facts — <c>OrderPlaced</c>, <c>CustomerRegistered</c>",
					"- because each records something that has already happened. Sentinel events break that rule on",
					"purpose: they do not describe a fact, they stand in for one. The canonical case is the type an",
					"aggregate deserializes to when it reads an event kind it does not recognize (for example",
					"<c>UnknownEvent</c>); genesis/uninitialized markers and legacy placeholders are the same shape."
				),
				CodeWriter.XmlInlinePara(
					$"Applying this attribute tells the analyzer the name is deliberate, so <c>{DiagnosticLibrary.EventNameShouldBePastTense.Id}</c>",
					"(\"event names should be past tense\") is suppressed for the annotated type. The attribute is read",
					"from the semantic model at compile time and has no runtime behavior — it is never inspected via",
					"reflection and does not need to be emitted."
				),
				CodeWriter.XmlInlinePara(
					"It suppresses only the naming diagnostic; all other event validation still applies. Do not use it",
					"to silence the warning on a genuine domain event whose name merely is not past tense yet — rename",
					"that event instead. The convention keeps the log readable as a history of facts, and every",
					"sentinel is a small hole in that guarantee."
				),
				CodeWriter.XmlInlinePara(
					$"<see cref=\"{typeof(AttributeUsageAttribute)}.Inherited\"/> is <see langword=\"false\"/>: sentinel status is",
					"per-type and is not conferred on derived records. Each type opts in explicitly."
				)
			)
			.XmlExample(
				"A fallback event used when an unrecognized event type is read back from the store:",
				CodeWriter.XmlInlineCodeBlock(
					"[SentinelEvent]",
					"public sealed record UnknownEvent(string OriginalTypeName, byte[] Payload);"
				),
				$"Without the attribute, <c>UnknownEvent</c> raises <c>{DiagnosticLibrary.EventNameShouldBePastTense.Id}</c> because \"Unknown\"",
				"is not a past-tense fact."
			);

		writer.AttributeClass(
			new(TypeLibrary.Purview.EventSourcing.Aggregates.SentinelEventAttribute) { IsSealed = true },
			AttributeTargets.Class | AttributeTargets.Struct,
			body =>
				body.XmlSummary(
						"An optional human-readable reason this event is a sentinel rather than a past-tense fact,",
						"recorded for audit and code-review purposes. Mirrors",
						$"{CodeWriter.XmlSee($"{typeof(System.Diagnostics.CodeAnalysis.SuppressMessageAttribute).FullName}.Justification")}."
					)
					.Property(
						new("Justification", PurviewTypeLibrary.System.String.MakeNullable(writer))
						{
							Accessibility = TypeDeclarationAccessibility.Public,
							IsInitOnly = true,
						}
					)
		);
	}

	static void AggregateDefaultsAttribute(CodeWriter writer)
	{
		writer.XmlSummary(
			$"Sets default source-generation options for all <see cref=\"{TypeLibrary.Purview.EventSourcing.Aggregates.AggregateAttribute}\" /> aggregates in an assembly.",
			"Aggregate-level options override these defaults."
		);

		writer.AttributeClass(
			new(TypeLibrary.Purview.EventSourcing.Aggregates.AggregateDefaultsAttribute) { IsSealed = true },
			AttributeTargets.Assembly,
			body =>
			{
				body.XmlSummary(
						$"Appends a suffix to generated event type names when no explicit <see cref=\"{TypeLibrary.Purview.EventSourcing.Aggregates.EventAttribute}.EventName\"/> is provided.",
						"Defaults to <c>Event</c>."
					)
					.Property(
						new("EventSuffix", PurviewTypeLibrary.System.String.MakeNullable(writer))
						{
							Accessibility = TypeDeclarationAccessibility.Public,
							HasSetter = true,
							IsInitOnly = true,
							Initializer = "\"Event\"",
						}
					);
				body.XmlSummary("Specifies the default base class for all aggregates in the assembly.")
					.Property(
						new("BaseType", PurviewTypeLibrary.System.Type.MakeNullable(writer))
						{
							Accessibility = TypeDeclarationAccessibility.Public,
							HasSetter = true,
							IsInitOnly = true,
						}
					);
			}
		);
	}

	static void CollectionEventAttribute(CodeWriter writer)
	{
		writer
			.XmlSummary(
				"Indicates the type of collection operation that a method represents",
				"when generating an event for a collection property."
			)
			.Enum(
				new(TypeLibrary.Purview.EventSourcing.Aggregates.CollectionEventOperation),
				[
					new("Auto", 0, "Automatically determine the operation type based on the method name."),
					new("Add", 1, "Indicates that the method represents an addition to a collection."),
					new("Remove", 2, "Indicates that the method represents a removal from a collection."),
				]
			);

		writer
			.XmlSummary(
				"Marks a method on a <see cref=\"CollectionEventAttribute\"/>-decorated class",
				"as a command that should have an event class and registration generated.",
				"<para>",
				"The method parameters become the event's properties. The generator creates:",
				"<list type=\"bullet\">",
				"<item>An event class named from the method using a deterministic past-tense convention, such as <c>NameChanged</c> or <c>CustomerRegistered</c></item>",
				"<item>A <c>Register&lt;{EventName}&gt;(Apply)</c> call in <c>RegisterEvents()</c></item>",
				"<item>An <c>Apply({EventName})</c> method that sets matching properties</item>",
				"<item>The method body calls <c>RecordAndApply(new {EventName} { ... })</c></item>",
				"</list>",
				"</para>"
			)
			.AttributeClass(
				new(TypeLibrary.Purview.EventSourcing.Aggregates.CollectionEventAttribute) { IsSealed = true },
				AttributeTargets.Method,
				body =>
				{
					body.XmlSummary(
							$"Constructs a new  {ToXmlCref(TypeLibrary.Purview.EventSourcing.Aggregates.CollectionEventAttribute)} with the specified collection property name."
						)
						.Constructor(
							new(
								TypeLibrary.Purview.EventSourcing.Aggregates.CollectionEventAttribute,
								TypeDeclarationAccessibility.Public
							)
							{
								Parameters = [new("propertyName", PurviewTypeLibrary.System.String)],
							},
							writeBody =>
							{
								writer.IfBlock(
									"string.IsNullOrWhiteSpace(propertyName)",
									ifBody =>
										ifBody.Throw(
											"new global::System.ArgumentException(\"Property name cannot be null or whitespace.\", nameof(propertyName))"
										)
								);

								writeBody.Assignment("PropertyName", "propertyName");
							}
						);

					body.XmlSummary(
							"The schema version of the generated event class. Defaults to 1.",
							"Increment when the event's properties change in a <b>breaking</b> way."
						)
						.Property(
							new("Version", PurviewTypeLibrary.System.Int32, TypeDeclarationAccessibility.Public)
							{
								HasSetter = true,
								IsInitOnly = true,
								Initializer = "1",
							}
						);

					body.XmlSummary(
							"Overrides the generated event type name for this method.",
							"Defaults to a deterministic past-tense event name inferred from the method name.",
							$"If not set, <see cref=\"AggregateAttribute.EventSuffix\"/> and <see cref=\"{TypeLibrary.Purview.EventSourcing.Aggregates.AggregateDefaultsAttribute}.EventSuffix\"/> may append a suffix."
						)
						.Property(
							new(
								"EventName",
								PurviewTypeLibrary.System.String.MakeNullable(writer),
								TypeDeclarationAccessibility.Public
							)
							{
								HasSetter = true,
								IsInitOnly = true,
							}
						);

					body.XmlSummary(
							"Overrides the generated event namespace for this method.",
							"Defaults to <c>{Aggregate-Namespace}.{Aggregate-Name-Without-Aggregate-Suffix}</c> unless overridden at aggregate level."
						)
						.Property(
							new(
								"EventNamespace",
								PurviewTypeLibrary.System.String.MakeNullable(writer),
								TypeDeclarationAccessibility.Public
							)
							{
								HasSetter = true,
								IsInitOnly = true,
							}
						);

					body.XmlSummary(
							"The property name of the collection property on the aggregate that this method modifies."
						)
						.Property(
							new("PropertyName", PurviewTypeLibrary.System.String, TypeDeclarationAccessibility.Public)
							{
								HasSetter = true,
								IsInitOnly = true,
							}
						);

					body.XmlSummary(
							$"Overrides collection mutation behavior. By default (<see cref=\"{TypeLibrary.Purview.EventSourcing.Aggregates.CollectionEventOperation}.Auto\"/>),",
							"methods starting with <c>Add</c> are treated as add mutations and methods starting with",
							"<c>Remove</c> or <c>Delete</c> are treated as remove mutations."
						)
						.Property(
							new(
								"Operation",
								TypeLibrary.Purview.EventSourcing.Aggregates.CollectionEventOperation,
								TypeDeclarationAccessibility.Public
							)
							{
								HasSetter = true,
								IsInitOnly = true,
								Initializer =
									TypeLibrary.Purview.EventSourcing.Aggregates.CollectionEventOperation.StaticMember(
										"Auto"
									),
							}
						);

					body.XmlSummary(
							"Indicates whether the event's apply method should be generated manually instead of automatically.",
							"",
							"If Manual is <langword>true</langword>, the generator will not create an <c>Apply({EventName})</c> method for this event.",
							"",
							"If Manual is <langword>true</langword>, the generator will not create an <c>Apply({EventName})</c> method for this event.",
							"",
							"An <c>Apply({Event-Name})</c> method must be implemented manually in the aggregate class to handle the event and update the aggregate's state, or",
							"the source generator will create an error indicating that the apply method is missing."
						)
						.Property(
							new("Manual", PurviewTypeLibrary.System.Boolean, TypeDeclarationAccessibility.Public)
							{
								HasSetter = true,
								IsInitOnly = true,
							}
						);
				}
			);
	}

	static void ComputedAttribute(CodeWriter writer)
	{
		writer.XmlSummary(
			"Marks an event parameter as a deterministic computed value.",
			"The parameter must be omitted by callers using the <see langword=\"default\"/> keyword",
			"and is finalized by the generated <c>OnComputing{EventName}</c> hook",
			"before the event is recorded."
		);

		writer.AttributeClass(
			new(TypeLibrary.Purview.EventSourcing.Aggregates.ComputedAttribute) { IsSealed = true },
			AttributeTargets.Parameter,
			bodyWriter => bodyWriter.Comment("Empty")
		);
	}

	static void EventAttribute(CodeWriter writer)
	{
		writer.XmlSummary(
			$"Marks a method on a <see cref=\"{TypeLibrary.Purview.EventSourcing.Aggregates.AggregateAttribute}\"/>-decorated class",
			"as a command that should have an event class and registration generated.",
			"<para>",
			"The method parameters become the event's properties. The generator creates:",
			"<list type=\"bullet\">",
			"<item>An event class named from the method using a deterministic past-tense convention, such as <c>NameChanged</c> or <c>CustomerRegistered</c></item>",
			"<item>A <c>Register&lt;{EventName}&gt;(Apply)</c> call in <c>RegisterEvents()</c></item>",
			"<item>An <c>Apply({EventName})</c> method that sets matching properties</item>",
			"<item>The method body calls <c>RecordAndApply(new {EventName} { ... })</c></item>",
			"</list>",
			"</para>"
		);

		writer.AttributeClass(
			new(TypeLibrary.Purview.EventSourcing.Aggregates.EventAttribute) { IsSealed = true },
			AttributeTargets.Method,
			body =>
			{
				body.XmlSummary(
						"The schema version of the generated event class. Defaults to 1.",
						"Increment when the event's properties change in a <b>breaking</b> way."
					)
					.Property(
						new("Version", PurviewTypeLibrary.System.Int32, TypeDeclarationAccessibility.Public)
						{
							HasSetter = true,
							IsInitOnly = true,
							Initializer = "1",
						}
					);

				body.XmlSummary(
						"Overrides the generated event type name for this method.",
						"Defaults to a deterministic past-tense event name inferred from the method name.",
						$"If not set, <see cref=\"{TypeLibrary.Purview.EventSourcing.Aggregates.AggregateAttribute}.EventSuffix\"/> and <see cref=\"{TypeLibrary.Purview.EventSourcing.Aggregates.AggregateAttribute}\"/> are used."
					)
					.Property(
						new(
							"EventName",
							PurviewTypeLibrary.System.String.MakeNullable(writer),
							TypeDeclarationAccessibility.Public
						)
						{
							HasSetter = true,
							IsInitOnly = true,
						}
					);

				body.XmlSummary(
						"Overrides the generated event namespace for this method.",
						"Defaults to <c>{Aggregate-Namespace}.{Aggregate-Name-Without-Aggregate-Suffix}</c> unless overridden at aggregate level."
					)
					.Property(
						new(
							"EventNamespace",
							PurviewTypeLibrary.System.String.MakeNullable(writer),
							TypeDeclarationAccessibility.Public
						)
						{
							HasSetter = true,
							IsInitOnly = true,
						}
					);

				body.XmlSummary(
						"Indicates whether the event's apply method should be generated manually instead of automatically.",
						"",
						"If Manual is <see langword=\"true\" />, the generator will not create an <c>Apply({EventName})</c> method for this event.",
						"",
						"An <c>Apply({Event-Name})</c> method must be implemented manually in the aggregate class to handle the event and update the aggregate's state, or",
						"the source generator will create an error indicating that the apply method is missing."
					)
					.Property(
						new("Manual", PurviewTypeLibrary.System.Boolean, TypeDeclarationAccessibility.Public)
						{
							HasSetter = true,
							IsInitOnly = true,
						}
					);
			}
		);
	}

	static void MetadataAttribute(CodeWriter writer)
	{
		writer
			.XmlSummary("Marks a parameter as metadata for the aggregate, indicating whether it should be stored.")
			.AttributeClass(
				new(TypeLibrary.Purview.EventSourcing.Aggregates.MetadataAttribute) { IsSealed = true },
				AttributeTargets.Parameter,
				body =>
				{
					body.XmlSummary(
							$"Constructs a new {ToXmlCref(TypeLibrary.Purview.EventSourcing.Aggregates.MetadataAttribute)} with the specified store value."
						)
						.Constructor(
							new(
								TypeLibrary.Purview.EventSourcing.Aggregates.MetadataAttribute,
								TypeDeclarationAccessibility.Public
							)
							{
								Parameters =
								[
									new("store", PurviewTypeLibrary.System.Boolean) { DefaultValue = "true" },
								],
							},
							writeBody => writeBody.Assignment("Store", "store")
						);
					body.XmlSummary("Indicates whether the metadata should be stored on the generated event or not.")
						.Property("Store", PurviewTypeLibrary.System.Boolean, TypeDeclarationAccessibility.Public);
				}
			);
	}

	static void PropertyAttribute(CodeWriter writer)
	{
		writer.XmlSummary(
			"Marks a property on an aggregate as a state property.",
			"The generator will create a matching property on the generated event class",
			"and set it in the <c>Apply({EventName})</c> method."
		);
		writer.AttributeClass(
			new(TypeLibrary.Purview.EventSourcing.Aggregates.PropertyAttribute) { IsSealed = true },
			AttributeTargets.Parameter,
			body =>
			{
				body.XmlSummary(
						$"Constructs a new {ToXmlCref(TypeLibrary.Purview.EventSourcing.Aggregates.PropertyAttribute)} with the specified property name."
					)
					.Constructor(
						new(
							TypeLibrary.Purview.EventSourcing.Aggregates.PropertyAttribute,
							TypeDeclarationAccessibility.Public
						)
						{
							Parameters = [new("propertyName", PurviewTypeLibrary.System.String)],
						},
						writeBody: writeBody =>
						{
							writer.IfBlock(
								"string.IsNullOrWhiteSpace(propertyName)",
								ifBody =>
									ifBody.Throw(
										"new global::System.ArgumentException(\"Property name cannot be null or whitespace.\", nameof(propertyName))"
									)
							);

							writeBody.Assignment("PropertyName", "propertyName");
						}
					);

				body.XmlSummary("The name of the property on the aggregate that this event property corresponds to.")
					.Property("PropertyName", PurviewTypeLibrary.System.String, TypeDeclarationAccessibility.Public);
			}
		);
	}

	static CodeWriter CreateCodeWriter(TypeIdentity namespaceType)
	{
		CodeWriter w = new(SourceGenLibrary.CreateGenerationSettings<AggregateSourceGenerator>());

		return w.AutoGeneratedHeader().FileScopedNamespace(namespaceType);
	}
}
