using System.Text.Json;
using Ferret.Abstractions;
using Ferret.Core.Backends.FullText;
using Ferret.Core.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ferret.Migrations.Tests.Annotations;

/// <summary>
/// Regression tests for migration DDL drifting from runtime search configuration.
///
/// The assembly overload of UseFerretSearchableAnnotations builds its own default
/// FullTextOptions/TrigramOptions/VectorOptions, so an app configuring non-default
/// options through AddFerret(...) generated DDL for the DEFAULTS — a silent mismatch
/// that produced empty results rather than an error. The DI-aware overload reads the
/// real configuration instead.
/// </summary>
public class ConfiguredOptionsReachDdlTests
{
    [SearchableEntity(Table = "articles")]
    [SearchGroup("content")]
    public sealed class Article : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "content")]
        public string Body { get; init; } = "";
    }

    private const string NonDefaultConfig = "spanish";
    private const string NonDefaultSuffix = "_ftsidecar";

    private static IServiceProvider BuildConfiguredServices() =>
        new ServiceCollection()
            .AddFerret(o => o
                .ScanAssembly(typeof(Article).Assembly)
                .UseFullTextSearch(ft =>
                {
                    ft.DefaultConfig = NonDefaultConfig;
                    ft.SidecarSuffix = NonDefaultSuffix;
                }))
            .BuildServiceProvider();

    private sealed class ConfiguredContext(DbContextOptions<ConfiguredContext> opts, IServiceProvider services)
        : DbContext(opts)
    {
        public DbSet<Article> Articles => Set<Article>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Article>().ToTable("articles");
            modelBuilder.UseFerretSearchableAnnotations(services, typeof(Article).Assembly);
        }
    }

    private sealed class DefaultsContext(DbContextOptions<DefaultsContext> opts) : DbContext(opts)
    {
        public DbSet<Article> Articles => Set<Article>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Article>().ToTable("articles");
            modelBuilder.UseFerretSearchableAnnotations(typeof(Article).Assembly);
        }
    }

    private static string GroupsJson(IModel model) =>
        (string)model.FindEntityType(typeof(Article))!
            .FindAnnotation(SearchableAnnotationKeys.FullTextGroupsV1)!.Value!;

    private static IModel BuildConfiguredModel()
    {
        var services = BuildConfiguredServices();
        var opts = new DbContextOptionsBuilder<ConfiguredContext>()
            .UseNpgsql("Host=localhost;Database=ferret_test")
            .Options;
        using var ctx = new ConfiguredContext(opts, services);
        return ctx.Model;
    }

    [Fact]
    public void Configured_full_text_config_reaches_generated_ddl()
    {
        var json = GroupsJson(BuildConfiguredModel());

        // DefaultConfig is resolved into the EntityRegistry at AddFerret time, so this asserts the
        // DI-built registry is used rather than one rebuilt here with default resolver defaults.
        json.Should().Contain(NonDefaultConfig,
            "DDL generated while AddFerret configured a non-default text-search config must use it, "
            + "otherwise the index never matches the query");
    }

    [Fact]
    public void Configured_sidecar_naming_reaches_generated_ddl()
    {
        var json = GroupsJson(BuildConfiguredModel());

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("SidecarTable").GetString()
            .Should().Be($"articles{NonDefaultSuffix}",
                "the sidecar table in DDL must match the one queries read");
    }

    [Fact]
    public void Assembly_overload_still_works_for_default_options()
    {
        var opts = new DbContextOptionsBuilder<DefaultsContext>()
            .UseNpgsql("Host=localhost;Database=ferret_test")
            .Options;
        using var ctx = new DefaultsContext(opts);

        // No service provider available: defaults are the only answer, and they are correct
        // for an app that also uses defaults. This must keep working.
        GroupsJson(ctx.Model).Should().Contain("articles_search");
    }

    [Fact]
    public void Provider_without_ferret_falls_back_to_defaults_rather_than_throwing()
    {
        // Design-time hosts can supply a service provider that has no Ferret registrations.
        // That must degrade to the default-options path, not fail — `dotnet ef` depends on it.
        var bare = new ServiceCollection().BuildServiceProvider();
        var opts = new DbContextOptionsBuilder<BareProviderContext>()
            .UseNpgsql("Host=localhost;Database=ferret_test")
            .Options;

        using var ctx = new BareProviderContext(opts, bare);
        var act = () => _ = ctx.Model;

        act.Should().NotThrow();
        GroupsJson(ctx.Model).Should().Contain("articles_search");
    }

    private sealed class BareProviderContext(DbContextOptions<BareProviderContext> opts, IServiceProvider services)
        : DbContext(opts)
    {
        public DbSet<Article> Articles => Set<Article>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Article>().ToTable("articles");
            modelBuilder.UseFerretSearchableAnnotations(services, typeof(Article).Assembly);
        }
    }

    [Fact]
    public void Unscanned_searchable_entity_fails_loud_rather_than_emitting_orphan_ddl()
    {
        // AddFerret scans an assembly that does not contain Orphan, so the runtime registry
        // will never know it — DDL generated for it could not be used by any query.
        var services = new ServiceCollection()
            .AddFerret(o => o
                .ScanAssembly(typeof(string).Assembly)
                .UseTrigramSearch())
            .BuildServiceProvider();

        var opts = new DbContextOptionsBuilder<OrphanContext>()
            .UseNpgsql("Host=localhost;Database=ferret_test")
            .Options;

        using var ctx = new OrphanContext(opts, services);
        var act = () => _ = ctx.Model;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not in the registry*");
    }

    [SearchableEntity(Table = "orphans")]
    public sealed class Orphan : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
    }

    private sealed class OrphanContext(DbContextOptions<OrphanContext> opts, IServiceProvider services)
        : DbContext(opts)
    {
        public DbSet<Orphan> Orphans => Set<Orphan>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Orphan>().ToTable("orphans");
            modelBuilder.UseFerretSearchableAnnotations(services, typeof(Orphan).Assembly);
        }
    }
}
