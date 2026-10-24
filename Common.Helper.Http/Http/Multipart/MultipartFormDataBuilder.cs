using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Common.Helper.Http;

/// <summary>
/// Построитель содержимого <c>multipart/form-data</c>: текстовые поля, JSON-части, файлы,
/// потоки и массивы байт. Используется из <see cref="HttpRequestBuilder.WithMultipartFormData"/>.
/// </summary>
public sealed class MultipartFormDataBuilder
{
    private const string DefaultContentType = "application/octet-stream";

    private readonly List<MultipartPart> _parts = [];

    /// <summary>Добавляет текстовое поле.</summary>
    /// <param name="name">Имя поля.</param>
    /// <param name="value">Значение; <c>null</c> заменяется пустой строкой.</param>
    public MultipartFormDataBuilder AddField(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _parts.Add(MultipartPart.Field(name, value ?? string.Empty));
        return this;
    }

    /// <summary>Добавляет JSON-часть, сериализованную с веб-настройками (camelCase).</summary>
    /// <typeparam name="T">Тип значения.</typeparam>
    /// <param name="name">Имя части.</param>
    /// <param name="value">Значение.</param>
    /// <param name="options">Необязательные параметры сериализации.</param>
    /// <remarks>
    /// Использует reflection-сериализатор. Для trimming/AOT используйте перегрузку
    /// с <see cref="JsonTypeInfo{T}"/>.
    /// </remarks>
    [RequiresUnreferencedCode("Сериализация JSON через reflection несовместима с полным триммингом и AOT. Используйте перегрузку с JsonTypeInfo<T>.")]
    [RequiresDynamicCode("Сериализация JSON через reflection может требовать генерации кода. Используйте перегрузку с JsonTypeInfo<T>.")]
    public MultipartFormDataBuilder AddJson<T>(string name, T value, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return AddJsonPart(name, JsonSerializer.Serialize(value, options ?? JsonSerializerOptions.Web));
    }

    /// <summary>
    /// Добавляет JSON-часть, используя метаданные сериализации из source-generated контекста.
    /// Безопасно для trimming и AOT.
    /// </summary>
    /// <typeparam name="T">Тип значения.</typeparam>
    /// <param name="name">Имя части.</param>
    /// <param name="value">Значение.</param>
    /// <param name="jsonTypeInfo">Метаданные типа из <see cref="JsonSerializerContext"/>.</param>
    public MultipartFormDataBuilder AddJson<T>(string name, T value, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        return AddJsonPart(name, JsonSerializer.Serialize(value, jsonTypeInfo));
    }

    /// <summary>Добавляет часть из массива байт с именем файла.</summary>
    /// <param name="name">Имя части.</param>
    /// <param name="content">Содержимое.</param>
    /// <param name="fileName">Имя файла.</param>
    /// <param name="contentType">Медиатип, по умолчанию <c>application/octet-stream</c>.</param>
    public MultipartFormDataBuilder AddBytes(
        string name,
        byte[] content,
        string fileName,
        string? contentType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        _parts.Add(MultipartPart.Bytes(name, content, contentType ?? DefaultContentType, fileName));
        return this;
    }

    /// <summary>Добавляет файл из потока.</summary>
    /// <param name="name">Имя части.</param>
    /// <param name="stream">Поток с содержимым файла.</param>
    /// <param name="fileName">Имя файла.</param>
    /// <param name="contentType">Медиатип, по умолчанию <c>application/octet-stream</c>.</param>
    public MultipartFormDataBuilder AddFile(
        string name,
        Stream stream,
        string fileName,
        string? contentType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        _parts.Add(MultipartPart.Stream(name, stream, contentType ?? DefaultContentType, fileName));
        return this;
    }

    /// <summary>Добавляет поток без имени файла (например, бинарное поле).</summary>
    /// <param name="name">Имя части.</param>
    /// <param name="stream">Поток с содержимым.</param>
    /// <param name="contentType">Медиатип, по умолчанию <c>application/octet-stream</c>.</param>
    public MultipartFormDataBuilder AddStream(string name, Stream stream, string? contentType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(stream);

        _parts.Add(MultipartPart.Stream(name, stream, contentType ?? DefaultContentType, fileName: null));
        return this;
    }

    internal MultipartFormDataContent Build()
    {
        var content = new MultipartFormDataContent();

        foreach (var part in _parts)
        {
            if (part.FileName is null)
            {
                content.Add(part.CreateContent(), part.Name);
            }
            else
            {
                content.Add(part.CreateContent(), part.Name, part.FileName);
            }
        }

        return content;
    }

    private MultipartFormDataBuilder AddJsonPart(string name, string json)
    {
        _parts.Add(MultipartPart.Json(name, json));
        return this;
    }
}
