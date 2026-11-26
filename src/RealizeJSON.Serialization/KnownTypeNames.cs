namespace RealizeJSON.Serialization;

/// <summary>Full metadata names of framework types with special serialization behavior.</summary>
public static class KnownTypeNames
{
    public const string DateTimeOffset = "System.DateTimeOffset";
    public const string DateOnly = "System.DateOnly";
    public const string TimeOnly = "System.TimeOnly";
    public const string TimeSpan = "System.TimeSpan";
    public const string Guid = "System.Guid";
    public const string Uri = "System.Uri";
    public const string Version = "System.Version";
    public const string Half = "System.Half";
    public const string Int128 = "System.Int128";
    public const string UInt128 = "System.UInt128";
    public const string BigInteger = "System.Numerics.BigInteger";
    public const string Type = "System.Type";
    public const string ValueTuple = "System.ValueTuple";
    public const string MemberInfo = "System.Reflection.MemberInfo";
    public const string Delegate = "System.Delegate";
    public const string Task = "System.Threading.Tasks.Task";
    public const string AsyncEnumerable = "System.Collections.Generic.IAsyncEnumerable`1";

    public const string IDictionary2 = "System.Collections.Generic.IDictionary`2";
    public const string IReadOnlyDictionary2 = "System.Collections.Generic.IReadOnlyDictionary`2";
    public const string IDictionary = "System.Collections.IDictionary";
    public const string IEnumerable = "System.Collections.IEnumerable";
    public const string ISet1 = "System.Collections.Generic.ISet`1";
    public const string IReadOnlySet1 = "System.Collections.Generic.IReadOnlySet`1";

    public const string JsonElement = "System.Text.Json.JsonElement";
    public const string JsonDocument = "System.Text.Json.JsonDocument";
    public const string JsonNode = "System.Text.Json.Nodes.JsonNode";
    public const string JToken = "Newtonsoft.Json.Linq.JToken";
    public const string ExpandoObject = "System.Dynamic.ExpandoObject";

    public const string DefaultValueAttribute = "System.ComponentModel.DefaultValueAttribute";
    public const string DataContractAttribute = "System.Runtime.Serialization.DataContractAttribute";
    public const string DataMemberAttribute = "System.Runtime.Serialization.DataMemberAttribute";
    public const string IgnoreDataMemberAttribute = "System.Runtime.Serialization.IgnoreDataMemberAttribute";
    public const string EnumMemberAttribute = "System.Runtime.Serialization.EnumMemberAttribute";
    public const string NonSerializedAttribute = "System.NonSerializedAttribute";
    public const string FlagsAttribute = "System.FlagsAttribute";
    public const string RequiredMemberAttribute = "System.Runtime.CompilerServices.RequiredMemberAttribute";
}
