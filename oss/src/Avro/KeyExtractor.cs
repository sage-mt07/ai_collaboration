using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KsqlDsl.Attributes;
using KsqlDsl.Modeling;

namespace KsqlDsl.Avro
{
    public static class KeyExtractor
    {
        public static object? ExtractKey<T>(T entity, EntityModel entityModel)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            if (entityModel == null)
                throw new ArgumentNullException(nameof(entityModel));

            var keyProperties = entityModel.KeyProperties;

            if (keyProperties.Length == 0)
                return null;

            if (keyProperties.Length == 1)
                return keyProperties[0].GetValue(entity);

            // 複合キー
            var keyRecord = new Dictionary<string, object?>();
            foreach (var prop in keyProperties.OrderBy(p => p.GetCustomAttribute<KeyAttribute>()?.Order ?? 0))
            {
                keyRecord[prop.Name] = prop.GetValue(entity);
            }
            return keyRecord;
        }

        public static Type DetermineKeyType(EntityModel entityModel)
        {
            if (entityModel == null)
                throw new ArgumentNullException(nameof(entityModel));

            var keyProperties = entityModel.KeyProperties;

            if (keyProperties.Length == 0)
                return typeof(string); // デフォルトキー

            if (keyProperties.Length == 1)
                return keyProperties[0].PropertyType;

            // 複合キーの場合はDictionaryとして扱う
            return typeof(Dictionary<string, object>);
        }

        public static bool IsCompositeKey(EntityModel entityModel)
        {
            return entityModel.KeyProperties.Length > 1;
        }

        public static PropertyInfo[] GetOrderedKeyProperties(EntityModel entityModel)
        {
            return entityModel.KeyProperties
                .OrderBy(p => p.GetCustomAttribute<KeyAttribute>()?.Order ?? 0)
                .ToArray();
        }

        public static string GetKeySchemaSubject(string topicName)
        {
            return $"{topicName}-key";
        }

        public static string GetValueSchemaSubject(string topicName)
        {
            return $"{topicName}-value";
        }
    }
}