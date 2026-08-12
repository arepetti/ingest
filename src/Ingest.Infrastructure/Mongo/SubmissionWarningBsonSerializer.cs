using Ingest.Core.Entities;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Ingest.Infrastructure.Mongo;

/// <summary>
/// Backward-compatible BSON serializer for <see cref="SubmissionWarning"/>. Warnings used to be
/// stored as a plain array of strings; they are now documents with a <c>valueName</c> and a
/// <c>message</c>. This serializer bridges both on-disk shapes so existing submissions keep
/// deserializing without any data migration:
/// <list type="bullet">
///   <item>a stored <b>string</b> becomes <c>SubmissionWarning(null, string)</c>;</item>
///   <item>a stored <b>document</b> is read field-by-field (tolerant of a missing/null value name);</item>
///   <item>everything is <b>written</b> as a document, so re-saving a legacy submission upgrades it.</item>
/// </list>
/// </summary>
internal sealed class SubmissionWarningBsonSerializer : SerializerBase<SubmissionWarning>
{
    private const string ValueNameField = "valueName";
    private const string MessageField = "message";
    private const string CodeField = "code";
    private const string ParamsField = "params";

    /// <inheritdoc />
    public override SubmissionWarning Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var reader = context.Reader;
        switch (reader.GetCurrentBsonType())
        {
            // Legacy shape: the warning was a bare string with no associated value.
            case BsonType.String:
                return new SubmissionWarning(null, reader.ReadString());

            case BsonType.Null:
                reader.ReadNull();
                return new SubmissionWarning(null, string.Empty);

            case BsonType.Document:
                string? valueName = null;
                var message = string.Empty;
                string? code = null;
                IReadOnlyDictionary<string, object?>? parameters = null;
                reader.ReadStartDocument();
                while (reader.ReadBsonType() != BsonType.EndOfDocument)
                {
                    var name = reader.ReadName(Utf8NameDecoder.Instance);
                    if (reader.GetCurrentBsonType() == BsonType.Null)
                    {
                        reader.ReadNull();
                        continue;
                    }
                    if (string.Equals(name, ValueNameField, StringComparison.OrdinalIgnoreCase))
                        valueName = reader.ReadString();
                    else if (string.Equals(name, MessageField, StringComparison.OrdinalIgnoreCase))
                        message = reader.ReadString();
                    else if (string.Equals(name, CodeField, StringComparison.OrdinalIgnoreCase))
                        code = reader.ReadString();
                    else if (string.Equals(name, ParamsField, StringComparison.OrdinalIgnoreCase) &&
                             reader.GetCurrentBsonType() == BsonType.Document)
                    {
                        var document = BsonDocumentSerializer.Instance.Deserialize(context);
                        parameters = document.Elements.ToDictionary(
                            x => x.Name,
                            x => (object?)BsonTypeMapper.MapToDotNetValue(x.Value),
                            StringComparer.Ordinal);
                    }
                    else
                        reader.SkipValue();
                }
                reader.ReadEndDocument();
                return new SubmissionWarning(valueName, message, code, parameters);

            default:
                // Anything unexpected: skip it and record a placeholder rather than throwing —
                // a warning is a diagnostic, it must never block reading a submission.
                reader.SkipValue();
                return new SubmissionWarning(null, string.Empty);
        }
    }

    /// <inheritdoc />
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, SubmissionWarning value)
    {
        var writer = context.Writer;
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartDocument();
        if (value.ValueName is not null)
        {
            writer.WriteName(ValueNameField);
            writer.WriteString(value.ValueName);
        }
        writer.WriteName(MessageField);
        writer.WriteString(value.Message ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(value.Code))
        {
            writer.WriteName(CodeField);
            writer.WriteString(value.Code);
        }
        if (value.Params is { Count: > 0 })
        {
            writer.WriteName(ParamsField);
            writer.WriteStartDocument();
            foreach (var (name, parameter) in value.Params)
            {
                writer.WriteName(name);
                BsonValueSerializer.Instance.Serialize(
                    context,
                    args,
                    BsonTypeMapper.MapToBsonValue(parameter));
            }
            writer.WriteEndDocument();
        }
        writer.WriteEndDocument();
    }
}
