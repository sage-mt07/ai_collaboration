using System;

namespace KsqlDsl.Avro
{
    public class AvroSerializerCacheKey : IEquatable<AvroSerializerCacheKey>
    {
        public Type EntityType { get; }
        public SerializerType Type { get; }
        public int SchemaId { get; }

        public AvroSerializerCacheKey(Type entityType, SerializerType type, int schemaId)
        {
            EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
            Type = type;
            SchemaId = schemaId;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(EntityType, Type, SchemaId);
        }

        public override bool Equals(object? obj)
        {
            return obj is AvroSerializerCacheKey other && Equals(other);
        }

        public bool Equals(AvroSerializerCacheKey? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return EntityType == other.EntityType && Type == other.Type && SchemaId == other.SchemaId;
        }

        public override string ToString()
        {
            return $"{EntityType.Name}:{Type}:{SchemaId}";
        }
    }

    public enum SerializerType
    {
        Key,
        Value
    }
}