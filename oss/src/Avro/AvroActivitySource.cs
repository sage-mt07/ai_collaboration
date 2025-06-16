using System.Diagnostics;

namespace KsqlDsl.Avro
{
    public class AvroActivitySource
    {
        private static readonly ActivitySource _activitySource = new("KsqlDsl.Avro", "1.0.0");

        public static Activity? StartSchemaRegistration(string subject)
        {
            return _activitySource.StartActivity("avro.schema.register")
                ?.SetTag("subject", subject)
                ?.SetTag("component", "schema-registry");
        }

        public static Activity? StartSerialization(string entityType, string serializerType)
        {
            return _activitySource.StartActivity("avro.serialize")
                ?.SetTag("entity.type", entityType)
                ?.SetTag("serializer.type", serializerType)
                ?.SetTag("component", "avro-serializer");
        }

        public static Activity? StartCacheOperation(string operation, string entityType)
        {
            return _activitySource.StartActivity($"avro.cache.{operation}")
                ?.SetTag("entity.type", entityType)
                ?.SetTag("component", "avro-cache");
        }
    }
}