using System.Collections.Generic;
using System.Linq;
using RealizeJSON.Core;
using RealizeJSON.Core.Contracts;
using RealizeJSON.SampleData;

namespace RealizeJSON.Serialization;

/// <summary>Builds Contract Inspector rows (included and excluded members) from a resolved contract.</summary>
public static class InspectorBuilder
{
    private const int MaxDepth = 4;
    private const int MaxRows = 500;

    public static IReadOnlyList<InspectorEntry> Build(JsonContract root)
    {
        var rows = new List<InspectorEntry>();
        var visiting = new HashSet<JsonContract>();
        Visit(root, "$", 0, rows, visiting);
        return rows;
    }

    private static void Visit(JsonContract contract, string path, int depth, List<InspectorEntry> rows, HashSet<JsonContract> visiting)
    {
        var target = Unwrap(contract);
        if (target is not ObjectContract obj || depth > MaxDepth || rows.Count >= MaxRows || !visiting.Add(obj))
        {
            return;
        }

        try
        {
            foreach (var member in obj.Members)
            {
                if (rows.Count >= MaxRows)
                {
                    return;
                }

                var jsonPath = member.Included && !member.IsExtensionData ? path + JsonEmitter.FormatPathSegment(member.JsonName) : null;
                var explanation = member.Explanations.Count > 0 ? string.Join(" ", member.Explanations) : (member.Included ? "Included by default member rules." : "Excluded.");
                rows.Add(new InspectorEntry(
                    member.ClrName,
                    member.DeclaringType,
                    member.Included ? member.JsonName : null,
                    member.ClrTypeDisplay,
                    jsonPath,
                    member.Included,
                    member.Provenance.Count > 0 ? string.Join("; ", member.Provenance) : "Default rules",
                    explanation,
                    member.Classification,
                    member.Location,
                    depth));

                if (member.Included && member.Contract is not null && jsonPath is not null)
                {
                    var childPath = Unwrap(member.Contract) is ObjectContract ? jsonPath : jsonPath + "[0]";
                    Visit(member.Contract, childPath, depth + 1, rows, visiting);
                }
            }
        }
        finally
        {
            visiting.Remove(obj);
        }
    }

    private static JsonContract Unwrap(JsonContract contract) => contract switch
    {
        CollectionContract c => Unwrap(c.Element),
        DictionaryContract d => Unwrap(d.Value),
        _ => contract,
    };

    /// <summary>Collects all members reachable from the root (for duplicate/sensitive checks).</summary>
    public static IEnumerable<PropertyContract> AllIncludedMembers(JsonContract root)
    {
        var seen = new HashSet<JsonContract>();
        var stack = new Stack<JsonContract>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var c = Unwrap(stack.Pop());
            if (c is not ObjectContract obj || !seen.Add(obj))
            {
                continue;
            }

            foreach (var m in obj.Members.Where(m => m.Included))
            {
                yield return m;
                if (m.Contract is not null)
                {
                    stack.Push(m.Contract);
                }
            }

            if (obj.Polymorphism is not null)
            {
                foreach (var v in obj.Polymorphism.Variants)
                {
                    if (v.Contract is not null)
                    {
                        stack.Push(v.Contract);
                    }
                }
            }
        }
    }
}
