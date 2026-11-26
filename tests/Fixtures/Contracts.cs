// Fixture types for RealizeJSON tests. Compiled by the tests with Roslyn; never part of the extension.
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using N = Newtonsoft.Json;

namespace Fixtures
{
    public class CustomerDTO
    {
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string FullName { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        [JsonIgnore]
        public string Password { get; set; } = string.Empty;
    }

    public enum OrderStatus { None = 0, Pending = 1, Shipped = 2, DarkBlueState = 3 }

    [Flags]
    public enum Permissions { None = 0, Read = 1, Write = 2, Delete = 4 }

    public class Primitives
    {
        public bool Flag { get; set; }
        public string Text { get; set; } = "";
        public char Letter { get; set; }
        public sbyte SByteValue { get; set; }
        public byte ByteValue { get; set; }
        public short ShortValue { get; set; }
        public ushort UShortValue { get; set; }
        public int IntValue { get; set; }
        public uint UIntValue { get; set; }
        public long LongValue { get; set; }
        public ulong ULongValue { get; set; }
        public float FloatValue { get; set; }
        public double DoubleValue { get; set; }
        public decimal Price { get; set; }
        public decimal DecimalValue { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateOnly BirthDate { get; set; }
        public TimeOnly OpensAt { get; set; }
        public TimeSpan Duration { get; set; }
        public Guid ExternalId { get; set; }
        public Uri Website { get; set; } = null!;
        public byte[] Payload { get; set; } = Array.Empty<byte>();
        public Version AppVersion { get; set; } = null!;
        public double Latitude { get; set; }
        public string Email { get; set; } = "";
    }

    public class Nullables
    {
        public int? MaybeCount { get; set; }
        public string? Nickname { get; set; }
        public DateTime? DeletedAt { get; set; }
        public OrderStatus? Status { get; set; }
        public Address? Shipping { get; set; }
    }

    public class Address
    {
        public string Street { get; set; } = "";
        public string City { get; set; } = "";
        public string PostalCode { get; set; } = "";
    }

    public class Order
    {
        public Guid OrderId { get; set; }
        public OrderStatus Status { get; set; }
        public Permissions Access { get; set; }
        public Address BillingAddress { get; set; } = new();
        public Address ShippingAddress { get; set; } = new();
        public List<OrderLine> Lines { get; set; } = new();
        public Dictionary<string, int> Quantities { get; set; } = new();
        public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
        public HashSet<int> Codes { get; set; } = new();
        public int[] Scores { get; set; } = Array.Empty<int>();
    }

    public class OrderLine
    {
        public string Sku { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public class Dictionaries
    {
        public Dictionary<string, string> ByName { get; set; } = new();
        public Dictionary<int, string> ById { get; set; } = new();
        public Dictionary<OrderStatus, int> ByStatus { get; set; } = new();
        public Dictionary<Guid, int> ByGuid { get; set; } = new();
        public IDictionary<string, Address> AddressBook { get; set; } = new Dictionary<string, Address>();
        public IReadOnlyDictionary<long, bool> Flags { get; set; } = new Dictionary<long, bool>();
    }

    public class BaseEntity
    {
        public int BaseId { get; set; }
        public virtual string Kind { get; set; } = "";
        public string Shadowed { get; set; } = "";
    }

    public class DerivedEntity : BaseEntity
    {
        public string Extra { get; set; } = "";
        public override string Kind { get; set; } = "";
        public new int Shadowed { get; set; }
    }

    public record PersonRecord(string FirstName, string LastName, int Age);

    public record struct PointRecord(double X, double Y);

    public struct Money
    {
        public decimal Amount { get; set; }
        public string Currency { get; set; }
    }

    public class WithStruct
    {
        public Money Total { get; set; }
        public Money? Discount { get; set; }
    }

    public class CreateDTO<T>
    {
        public string RequestId { get; set; } = "";
        public T Payload { get; set; } = default!;
        public List<T> History { get; set; } = new();
    }

    public class CaseDTO
    {
        public int CaseNumber { get; set; }
        public string Title { get; set; } = "";
    }

    public class GenericHolder
    {
        public CreateDTO<CaseDTO> Create { get; set; } = new();
        public CreateDTO<List<Dictionary<string, CaseDTO>>> Nested { get; set; } = new();
    }

    public class StjAttributes
    {
        [JsonPropertyOrder(2)] public int Last { get; set; }
        [JsonPropertyOrder(-1)] public int First { get; set; }
        public int Middle { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OptionalNote { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Retries { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? AlwaysWritten { get; set; }
        [JsonInclude] public string IncludedField = "x";
        public string PlainField = "y";
        [JsonInclude] internal int InternalCounter { get; set; }
        [JsonRequired] public string Code { get; set; } = "";
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public int Quantity { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))] public OrderStatus Status { get; set; }
        public string ReadOnlyProperty { get; } = "init";
        public int WriteOnly { set { } }
        private string Secret { get; set; } = "";
        public static int StaticCount { get; set; }
        public int this[int index] => index;
    }

    public class WithFields
    {
        public int PublicField;
        public readonly int ReadOnlyField;
        private int _privateField;
        public int Property { get; set; }
        public int GetPrivate() => _privateField;
    }

    public class WithExtensionData
    {
        public int Id { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    public class UnknownConverterTarget
    {
        [JsonConverter(typeof(MyConverter))] public string Value { get; set; } = "";
    }

    public class MyConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new InvalidOperationException("never executed");
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => throw new InvalidOperationException("never executed");
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(Dog), "dog")]
    [JsonDerivedType(typeof(Cat), "cat")]
    public abstract class Animal
    {
        public string Name { get; set; } = "";
    }

    public sealed class Dog : Animal
    {
        public bool GoodBoy { get; set; }
    }

    public sealed class Cat : Animal
    {
        public int Lives { get; set; }
    }

    public class Zoo
    {
        public List<Animal> Animals { get; set; } = new();
    }

    public class TreeNode
    {
        public string Label { get; set; } = "";
        public TreeNode? Parent { get; set; }
        public List<TreeNode> Children { get; set; } = new();
    }

    public class Siblings
    {
        public Address Home { get; set; } = new();
        public Address Work { get; set; } = new();
    }

    public class Deep1 { public Deep2 Next { get; set; } = new(); }
    public class Deep2 { public Deep3 Next { get; set; } = new(); }
    public class Deep3 { public Deep4 Next { get; set; } = new(); }
    public class Deep4 { public string Leaf { get; set; } = ""; }

    public class Wide
    {
        public List<Order> Orders { get; set; } = new();
    }

    public class Unsupported
    {
        public object? Anything { get; set; }
        public dynamic? Dyn { get; set; }
        public IntPtr Handle { get; set; }
        public Type? ClrType { get; set; }
        public Action? Callback { get; set; }
        public int[,]? Grid { get; set; }
        public JsonElement Raw { get; set; }
    }

    public interface IShape
    {
        double Area { get; }
    }

    public class ShapeHolder
    {
        public IShape? Shape { get; set; }
    }

    public class Tuples
    {
        public (int Count, string Label) Pair { get; set; }
        public Tuple<int, string>? Classic { get; set; }
        public KeyValuePair<string, int> Entry { get; set; }
    }

    public class Escaping
    {
        public string Html { get; set; } = "";
    }

    // Security fixtures: constructors, getters and static constructors with observable side effects.
    public static class SideEffects
    {
        public static int Count;
    }

    public class Explosive
    {
        static Explosive() { SideEffects.Count++; }

        public Explosive() { SideEffects.Count++; throw new InvalidOperationException("constructor executed"); }

        public string Value
        {
            get { SideEffects.Count++; throw new InvalidOperationException("getter executed"); }
            set { SideEffects.Count++; }
        }

        [JsonConverter(typeof(MyConverter))]
        public string Converted { get; set; } = "";
    }

    // Newtonsoft-specific fixtures.
    public class NsAttributes
    {
        [N.JsonProperty("display_name")] public string DisplayName { get; set; } = "";
        [N.JsonProperty(Order = 2)] public int Second { get; set; }
        [N.JsonProperty(Order = -2)] public int First { get; set; }
        public int Unordered { get; set; }
        [N.JsonIgnore] public string Hidden { get; set; } = "";
        [N.JsonProperty(NullValueHandling = N.NullValueHandling.Ignore)] public string? MaybeNull { get; set; }
        [DefaultValue(5)] [N.JsonProperty(DefaultValueHandling = N.DefaultValueHandling.Ignore)] public int Retries { get; set; }
        [N.JsonProperty] private string PrivateIncluded { get; set; } = "";
        public int PublicField;
        [N.JsonConverter(typeof(N.Converters.StringEnumConverter))] public OrderStatus Status { get; set; }
        public string ReadOnly { get; } = "init";
    }

    [N.JsonObject(N.MemberSerialization.OptIn)]
    public class NsOptIn
    {
        [N.JsonProperty] public int Included { get; set; }
        public int NotIncluded { get; set; }
    }

    [DataContract]
    public class NsDataContract
    {
        [DataMember(Name = "id", Order = 1)] public int Identifier { get; set; }
        [DataMember(Order = 0)] public string Label { get; set; } = "";
        public string NotAMember { get; set; } = "";
        [DataMember(EmitDefaultValue = false)] public string? Optional { get; set; }
    }

    public class NsShouldSerialize
    {
        public string Name { get; set; } = "";
        public bool ShouldSerializeName() => Name.Length > 0;
    }

    public class EnumMembers
    {
        public ApiState State { get; set; }
    }

    public enum ApiState
    {
        [EnumMember(Value = "in-progress")] InProgress = 1,
        Done = 2,
    }

    public class HalfValues
    {
        public Half Ratio { get; set; }
    }

    public class ComputedMembers
    {
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Total => Quantity * UnitPrice;
        public string Label { get { return "x"; } }
    }

    public class NamingSamples
    {
        public string URLValue { get; set; } = "";
        public string IOStream { get; set; } = "";
        public string HTMLParser2Go { get; set; } = "";
        public string X1Y2 { get; set; } = "";
        public string Prop_Name { get; set; } = "";
        public string myProp { get; set; } = "";
        public string AB1 { get; set; } = "";
    }

    public class MultiDim
    {
        public int[,] Grid { get; set; } = new int[0, 0];
    }

    public class BadKeys
    {
        public Dictionary<Address, int> ByAddress { get; set; } = new();
    }

    public class WithConstructor
    {
        [JsonConstructor]
        [N.JsonConstructor]
        public WithConstructor(int id) { Id = id; }

        public int Id { get; }
    }

    public class SensitiveData
    {
        public string UserName { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string Shipping { get; set; } = "";
    }

    public class EscapingSamples
    {
        public string Text { get; set; } = "";
    }

    public class DuplicateNames
    {
        [JsonPropertyName("value")] public int A { get; set; }
        [JsonPropertyName("value")] public int B { get; set; }
    }
}
