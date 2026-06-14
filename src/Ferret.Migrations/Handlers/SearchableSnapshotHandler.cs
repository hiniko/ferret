using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ferret.Migrations.Handlers;

[CustomMigrationHandler(Order = 100)]
public sealed class SearchableSnapshotHandler : IMigrationsSnapshotHandler
{
    public void GenerateSnapshot(IModel model, IndentedStringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(builder);

        // Model-root annotation
        var rootExt = model.FindAnnotation(SearchableAnnotationKeys.RequiredExtensionsV1);
        if (rootExt?.Value is string extJson)
        {
            builder.Append("modelBuilder.HasAnnotation(\"")
                .Append(SearchableAnnotationKeys.RequiredExtensionsV1)
                .Append("\", ")
                .Append(QuoteCSharpString(extJson))
                .AppendLine(");");
        }

        // Property-level annotations
        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var annotation = property.FindAnnotation(SearchableAnnotationKeys.SearchableIndexV1);
                if (annotation?.Value is not string json) continue;

                builder
                    .Append("modelBuilder.Entity(\"")
                    .Append(entityType.ClrType.FullName ?? entityType.Name)
                    .Append("\", b => b.Property<")
                    .Append(CSharpTypeName(property.ClrType))
                    .Append(">(\"")
                    .Append(property.Name)
                    .Append("\").HasAnnotation(\"")
                    .Append(SearchableAnnotationKeys.SearchableIndexV1)
                    .Append("\", ")
                    .Append(QuoteCSharpString(json))
                    .AppendLine("));" );
            }
        }

        // Entity-level full-text group annotations
        foreach (var entityType in model.GetEntityTypes())
        {
            var groupAnn = entityType.FindAnnotation(SearchableAnnotationKeys.FullTextGroupsV1);
            if (groupAnn?.Value is not string groupJson) continue;
            builder
                .Append("modelBuilder.Entity(\"")
                .Append(entityType.ClrType.FullName ?? entityType.Name)
                .Append("\", b => b.HasAnnotation(\"")
                .Append(SearchableAnnotationKeys.FullTextGroupsV1)
                .Append("\", ")
                .Append(QuoteCSharpString(groupJson))
                .AppendLine("));" );
        }
    }

    private static string CSharpTypeName(Type type) => type switch
    {
        _ when type == typeof(string)  => "string",
        _ when type == typeof(bool)    => "bool",
        _ when type == typeof(byte)    => "byte",
        _ when type == typeof(short)   => "short",
        _ when type == typeof(int)     => "int",
        _ when type == typeof(long)    => "long",
        _ when type == typeof(float)   => "float",
        _ when type == typeof(double)  => "double",
        _ when type == typeof(decimal) => "decimal",
        _ when type == typeof(char)    => "char",
        _ when type == typeof(object)  => "object",
        _ => type.Name,
    };

    private static string QuoteCSharpString(string value)
    {
        // Use C# raw-string literal with """ to avoid escaping. Pad with extra quotes if the value
        // already contains """ (extremely unlikely for our JSON payloads).
        var marker = "\"\"\"";
        while (value.Contains(marker))
        {
            marker += "\"";
        }
        return marker + value + marker;
    }
}
