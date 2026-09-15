using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Purview.EventSourcing;

/// <summary>
/// Helper methods for <see cref="Type"/> instances.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[DebuggerStepThrough]
public static partial class TypeNameHelper
{
	[GeneratedRegex("(?<!^)([A-Z][a-z]|(?<=[a-z])[A-Z])", RegexOptions.Compiled)]
	private static partial Regex TitleSplitCaseRegex();

	static readonly Regex TitleCaseSplit = TitleSplitCaseRegex();

	static readonly ConcurrentDictionary<(string Name, string TrimPart), string> NameCache = new();

	/// <summary>
	/// <para>
	/// Converts a <see cref="Type"/>'s (via <paramref name="objectType"/>) minus
	/// the <paramref name="trimPart"/> into a lowered title-case split and combined with '-' string.
	/// </para>
	/// <para>
	/// For example, given a <paramref name="objectType"/> of A.Namespace.LearningHTMLTestAggregate,
	/// and a <paramref name="trimPart"/> of 'Aggregate', then the result would be:
	/// </para>
	/// <para>learning-html-test</para>
	/// </summary>
	/// <param name="objectType">The <see cref="Type"/> to use for it's <see cref="Reflection.MemberInfo.Name"/>.</param>
	/// <param name="trimPart">The suffix to trim.</param>
	/// <param name="fallThroughToFullTypeName">If the resulting value is invalid, and this parameter is true then this
	/// the <see cref="Type.FullName"/> is returned, other the
	/// <see cref="Type.Name"/> is returned. This is also true if the full name is not valid. Defaults to false.</param>
	/// <returns>The converted string based on the <paramref name="objectType"/> and <paramref name="trimPart"/>.</returns>
	public static string GetName(Type objectType, string trimPart, bool fallThroughToFullTypeName = false)
	{
		ArgumentNullException.ThrowIfNull(objectType, nameof(objectType));
		ArgumentException.ThrowIfNullOrWhiteSpace(trimPart, nameof(trimPart));

		var name = objectType.Name;
		if (name.EndsWith(trimPart, StringComparison.InvariantCulture))
		{
			var result = name[..^trimPart.Length];
			if (result.Length > 0)
				return NameCache.GetOrAdd((name, trimPart), static (_, value) => ConvertTitleSplit(value), result);
		}

		return fallThroughToFullTypeName ? objectType.FullName ?? objectType.Name : name;
	}

	static string ConvertTitleSplit(string result)
	{
#pragma warning disable CA1308 // Normalize strings to uppercase
		return TitleCaseSplit.Replace(result, "-$1").ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase
	}
}
