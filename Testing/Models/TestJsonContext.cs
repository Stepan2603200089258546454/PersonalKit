using System.Text.Json.Serialization;

namespace Common.Helper.Http.Tests.Models;

/// <summary>
/// Source-generated контекст сериализации: проверяет AOT/trim-friendly перегрузки
/// с <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}"/>.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ItemDto))]
[JsonSerializable(typeof(ProblemDto))]
internal sealed partial class TestJsonContext : JsonSerializerContext;
