using KsqlDsl.SchemaRegistry;
using KsqlDsl.SchemaRegistry.Implementation;
using KsqlDsl.Services;
using System;

namespace KsqlDsl.Options;

public static class KafkaContextOptionsBuilderExtensions
{
    public static KafkaContextOptionsBuilder EnableAvroSchemaAutoRegistration(this KafkaContextOptionsBuilder builder, bool forceRegistration = false)
    {
        var options = builder.GetOptions();
        options.EnableAutoSchemaRegistration = true;
        options.ForceSchemaRegistration = forceRegistration;
        return builder;
    }

    public static KafkaContextOptionsBuilder DisableAvroSchemaAutoRegistration(this KafkaContextOptionsBuilder builder)
    {
        var options = builder.GetOptions();
        options.EnableAutoSchemaRegistration = false;
        return builder;
    }

    public static KafkaContextOptionsBuilder UseCustomSchemaRegistryClient(this KafkaContextOptionsBuilder builder, ISchemaRegistryClient schemaRegistryClient)
    {
        if (schemaRegistryClient == null)
            throw new ArgumentNullException(nameof(schemaRegistryClient));

        var options = builder.GetOptions();
        options.CustomSchemaRegistryClient = schemaRegistryClient;
        return builder;
    }

    public static KafkaContextOptionsBuilder UseSchemaRegistryAuthentication(this KafkaContextOptionsBuilder builder, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username cannot be null or empty", nameof(username));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password cannot be null or empty", nameof(password));

        var options = builder.GetOptions();
        options.SchemaRegistryUsername = username;
        options.SchemaRegistryPassword = password;
        return builder;
    }

    public static KafkaContextOptionsBuilder ConfigureSchemaGeneration(this KafkaContextOptionsBuilder builder, SchemaGenerationOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        var builderOptions = builder.GetOptions();
        builderOptions.SchemaGenerationOptions = options;
        return builder;
    }
}