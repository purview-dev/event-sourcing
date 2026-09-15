namespace Purview.EventSourcing.SourceGenerator.Aggregate;

static partial class CommandMethodEmitter
{
	public static void Generate(AggregateEmitContext outputContext, CodeWriter writer, AggregateEventMethodInfo method)
	{
		var hookSuffix = AggregateSourceEmitter.GetHookName(method.EventType);

		writer.Method(
			new(method.MethodName, method.ReturnType)
			{
				Accessibility = method.MethodAccessibility.ToTypeDeclarationAccessibility(),
				IsPartial = true,
				Parameters =
				[
					.. method.AllParameters.Select(static p => new ParameterDeclarationOptions(
						p.ParameterName,
						p.ParameterType
					)),
				],
			},
			writeBody =>
			{
				if (!method.AllParameters.IsEmpty)
					EmitParameterPreparationBlock(outputContext, writeBody, method);

				if (!method.ComputedParameters.IsEmpty)
					EmitOnComputingBefore(writeBody, method, hookSuffix);

				EmitEventCreationAndShouldApply(writeBody, method, hookSuffix, declareVariable: true);

				EmitRaisingHook(writeBody, method, hookSuffix);

				if (!method.ComputedParameters.IsEmpty)
					EmitOnComputingAfter(writeBody, method, hookSuffix);

				// Reflect any ref-parameter mutations the hooks made into the single event instance,
				// then re-evaluate the guard against the post-hook values.
				AggregateSourceEmitter.EmitEventSync(writeBody, method);

				writer.IfBlock(
					$"!ShouldApply{hookSuffix}(@event)",
					ifBody => AggregateSourceEmitter.EmitNoChangeReturn(ifBody, method.ReturnKind)
				);

				if (!method.AllParameters.IsEmpty)
				{
					var mappedParameters = method
						.AllParameters.Where(static parameter => parameter.HasAggregateProperty)
						.ToList();

					if (mappedParameters.Count > 0)
						EmitUnchangedCheck(writeBody, method, mappedParameters);
				}

				EmitFinalization(writeBody, method, hookSuffix);
			}
		);

		EmitHookDeclarations(writer, method, hookSuffix);
	}

	static void EmitParameterPreparationBlock(
		AggregateEmitContext outputContext,
		CodeWriter writer,
		AggregateEventMethodInfo method
	)
	{
		foreach (var prop in method.AllParameters)
		{
			if (prop.IsComputed)
			{
				writer.Assignment("var", AggregateSourceEmitter.GetLocalValueName(prop), prop.ParameterName);
				continue;
			}

			if (prop.ParameterConversionKind is not EventParameterConversionKind.None)
			{
				writer.Assignment(
					"var",
					AggregateSourceEmitter.GetLocalValueName(prop),
					AggregateSourceEmitter.BuildPropertyValueExpression(outputContext, method, prop)
				);
				continue;
			}
		}

		if (
			method.AllParameters.Any(static p =>
				p.IsComputed || p.ParameterConversionKind is not EventParameterConversionKind.None
			)
		)
			writer.NewLine();

		var computedParameters = method.AllParameters.Where(static parameter => parameter.IsComputed).ToList();

		foreach (var prop in computedParameters)
		{
			var ifCondition =
				$"!global::System.Collections.Generic.EqualityComparer<{prop.PropertyType}>.Default.Equals({prop.ParameterName}, default({prop.PropertyType}))";

			writer.IfBlock(
				ifCondition,
				ifBody =>
					ifBody.Throw(
						$"new global::System.ArgumentException(\"Computed parameter '{prop.ParameterName}' cannot be set by callers.\", nameof({prop.ParameterName}))"
					)
			);
		}

		EmitValidationGuards(writer, method);

		foreach (var prop in method.AllParameters)
		{
			if (!prop.HasAggregateProperty)
				continue;

			writer
				.Comment($"Invoke On{prop.AggregatePropertyName}Changing hook for parameter '{prop.ParameterName}'")
				.MethodCall(
					$"On{prop.AggregatePropertyName}Changing",
					[
						new MethodCallArgumentOptions(
							AggregateSourceEmitter.GetWorkingValueName(prop),
							ParameterModifier.Ref
						),
					]
				);
		}
	}

	static void EmitValidationGuards(CodeWriter writer, AggregateEventMethodInfo method)
	{
		foreach (var prop in method.AllParameters)
		{
			if (prop.IsComputed)
				continue;

			if (prop.ParameterConversionKind is not EventParameterConversionKind.None)
				continue;

			if (!prop.RequiresLocalCopy)
				continue;

			if (prop.IsRequired && prop.IsString)
			{
				writer.IfBlock(
					$"global::System.String.IsNullOrWhiteSpace({prop.ParameterName})",
					ifBody =>
						ifBody.Throw(
							$"new global::System.ArgumentException(\"Parameter '{prop.ParameterName}' cannot be null or empty.\", nameof({prop.ParameterName}))"
						)
				);
			}
			else if (prop.IsRequired || prop.IsNotNull)
			{
				writer.IfBlock(
					$"{prop.ParameterName} is null",
					ifBody => ifBody.Throw($"new global::System.ArgumentNullException(nameof({prop.ParameterName}))")
				);
			}

			writer.Assignment(
				"var",
				AggregateSourceEmitter.GetLocalValueName(prop),
				prop.ParameterName,
				forceNotNull: true
			);
		}
	}

	static void EmitOnComputingBefore(CodeWriter writer, AggregateEventMethodInfo method, string hookSuffix)
	{
		writer.MethodCall(
			$"OnComputing{hookSuffix}",
			AggregateSourceEmitter.BuildOnCreatingCallArgumentList(method.ComputedParameters)
		);

		if (!method.NonComputedParameters.IsEmpty)
		{
			writer.MethodCall(
				$"OnComputing{hookSuffix}",
				AggregateSourceEmitter.BuildOnCreatingCallArgumentList(method.NonComputedParameters)
			);
		}

		writer.MethodCall(
			$"OnComputing{hookSuffix}",
			AggregateSourceEmitter.BuildOnCreatingCallArgumentList(method.AllParameters)
		);
	}

	static void EmitEventCreationAndShouldApply(
		CodeWriter writer,
		AggregateEventMethodInfo method,
		string hookSuffix,
		bool declareVariable
	)
	{
		AggregateSourceEmitter.EmitEventCreation(writer, method, declareVariable);

		writer.IfBlock(
			$"!ShouldApply{hookSuffix}(@event)",
			ifBody => AggregateSourceEmitter.EmitNoChangeReturn(ifBody, method.ReturnKind)
		);
	}

	static void EmitRaisingHook(CodeWriter writer, AggregateEventMethodInfo method, string hookSuffix)
	{
		var onRaisingMethodName = $"OnRaising{hookSuffix}";
		if (method.AllParameters.IsEmpty)
			writer.MethodCall(onRaisingMethodName);
		else if (!method.ComputedParameters.IsEmpty)
		{
			if (method.NonComputedParameters.IsEmpty)
				writer.MethodCall(onRaisingMethodName);
			else
			{
				writer.MethodCall(
					onRaisingMethodName,
					AggregateSourceEmitter.BuildOnCreatingCallArgumentList(method.NonComputedParameters)
				);
			}

			writer.MethodCall(
				onRaisingMethodName,
				AggregateSourceEmitter.BuildOnCreatingCallArgumentList(method.AllParameters)
			);
		}
		else
		{
			writer.MethodCall(
				onRaisingMethodName,
				AggregateSourceEmitter.BuildOnCreatingCallArgumentList(method.AllParameters)
			);
		}
	}

	static void EmitOnComputingAfter(CodeWriter writer, AggregateEventMethodInfo method, string hookSuffix)
	{
		var onComputingMethodName = $"OnComputing{hookSuffix}";

		writer.MethodCall(
			onComputingMethodName,
			AggregateSourceEmitter.BuildOnCreatingCallArgumentList(method.ComputedParameters)
		);

		if (!method.NonComputedParameters.IsEmpty)
		{
			writer.MethodCall(
				onComputingMethodName,
				AggregateSourceEmitter.BuildOnCreatingCallArgumentList(method.NonComputedParameters)
			);
		}

		writer.MethodCall(
			onComputingMethodName,
			AggregateSourceEmitter.BuildOnCreatingCallArgumentList(method.AllParameters)
		);
	}

	static void EmitUnchangedCheck(
		CodeWriter writer,
		AggregateEventMethodInfo method,
		List<EventPropertyInfo> mappedParameters
	)
	{
		writer.IfBlock(
			AggregateSourceEmitter.BuildUnchangedCondition(mappedParameters),
			ifBody => AggregateSourceEmitter.EmitNoChangeReturn(ifBody, method.ReturnKind)
		);
	}

	static void EmitFinalization(CodeWriter writer, AggregateEventMethodInfo method, string hookSuffix)
	{
		writer.MethodCall($"OnRaised{hookSuffix}", ["@event"]);
		writer.MethodCall($"RecordAndApply", ["@event"]);

		AggregateSourceEmitter.EmitSuccessReturn(writer, method.ReturnKind);
	}

	static void EmitHookDeclarations(CodeWriter writer, AggregateEventMethodInfo method, string hookSuffix)
	{
		var suppression = AggregateSourceEmitter.CreateCA1822Suppression();

		if (!method.ComputedParameters.IsEmpty)
		{
			var computingMethodName = $"OnComputing{hookSuffix}";
			writer.PartialMethod(
				new(computingMethodName)
				{
					Attributes = [suppression],
					Parameters = AggregateSourceEmitter.BuildOnCreatingDeclarationParameterList(
						method.ComputedParameters
					),
				}
			);

			if (!method.NonComputedParameters.IsEmpty)
			{
				writer.PartialMethod(
					new(computingMethodName)
					{
						Attributes = [suppression],
						Parameters = AggregateSourceEmitter.BuildOnCreatingDeclarationParameterList(
							method.NonComputedParameters
						),
					}
				);
			}

			writer.PartialMethod(
				new(computingMethodName)
				{
					Attributes = [suppression],
					Parameters = AggregateSourceEmitter.BuildOnCreatingDeclarationParameterList(method.AllParameters),
				}
			);
		}

		var raisingMethodName = $"OnRaising{hookSuffix}";
		if (method.AllParameters.IsEmpty)
		{
			writer.PartialMethod(new(raisingMethodName) { Attributes = [suppression] });
		}
		else if (!method.ComputedParameters.IsEmpty)
		{
			if (method.NonComputedParameters.IsEmpty)
				writer.PartialMethod(new(raisingMethodName) { Attributes = [suppression] });
			else
			{
				writer.PartialMethod(
					new(raisingMethodName)
					{
						Attributes = [suppression],
						Parameters = AggregateSourceEmitter.BuildOnCreatingDeclarationParameterList(
							method.NonComputedParameters
						),
					}
				);
			}

			writer.PartialMethod(
				new(raisingMethodName)
				{
					Attributes = [suppression],
					Parameters = AggregateSourceEmitter.BuildOnCreatingDeclarationParameterList(method.AllParameters),
				}
			);
		}
		else
		{
			writer.PartialMethod(
				new(raisingMethodName)
				{
					Attributes = [suppression],
					Parameters = AggregateSourceEmitter.BuildOnCreatingDeclarationParameterList(method.AllParameters),
				}
			);
		}

		writer.Method(
			new("ShouldApply" + hookSuffix, PurviewTypeLibrary.System.Boolean)
			{
				Parameters = [new ParameterDeclarationOptions("@event", method.EventType)],
			},
			writeBody =>
			{
				writeBody.Assignment("var", "shouldApply", "true");
				writeBody.MethodCall("OnShouldApply" + hookSuffix, ["@event", "ref shouldApply"]);
				writeBody.Return("shouldApply");
			}
		);

		writer.PartialMethod(
			new("OnShouldApply" + hookSuffix)
			{
				Attributes = [suppression],
				Parameters =
				[
					new("@event", method.EventType),
					new("shouldApply", PurviewTypeLibrary.System.Boolean) { Modifier = ParameterModifier.Ref },
				],
			}
		);

		writer.PartialMethod(
			new("OnRaised" + hookSuffix) { Attributes = [suppression], Parameters = [new("@event", method.EventType)] }
		);

		writer.PartialMethod(
			new("OnApplied" + hookSuffix) { Attributes = [suppression], Parameters = [new("@event", method.EventType)] }
		);
	}
}
