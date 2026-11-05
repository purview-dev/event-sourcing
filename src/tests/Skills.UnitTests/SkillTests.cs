using Purview.Skills.Testing;

namespace Purview.EventSourcing.Skills;

/// <summary>
/// Validates the agent skills this repository authors under <c>skills/</c>.
/// </summary>
/// <remarks>
/// <see cref="InheritsTestsAttribute"/> is load-bearing: TUnit does not walk base types during
/// discovery, so without it this class compiles, the run is green, and nothing is validated.
/// </remarks>
[InheritsTests]
public sealed class SkillTests : PurviewSkillTests;
