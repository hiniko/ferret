using System.Linq.Expressions;
using Ferret.Abstractions;
using Ferret.Core.Engine.MultiSearch;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine;

public class ProjectionTranslatorTests
{
    [SearchableEntity]
    private sealed class Author : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
        public string? Email { get; init; }
        public int Age { get; init; }
        public Author? Friend { get; init; }
    }

    private sealed class AuthorHit : MultiSearchHit
    {
        public AuthorHit() { }

        public AuthorHit(string title, string? subtitle)
        {
            Title = title;
            Subtitle = subtitle;
        }

        public string Title { get; set; } = "";
        public string? Subtitle { get; set; }
        public int Age { get; set; }
        public string Badge { get; set; } = "";
    }

    private static EntityModel Model() => EntityRegistry
        .Build([typeof(Author)], new SnakeCaseNamingStrategy())
        .Get<Author>();

    private static CompiledProjection Translate(Expression<Func<Author, AuthorHit>> projection) =>
        ProjectionTranslator.Translate(projection, Model());

    [Fact]
    public void member_init_maps_properties_to_columns_in_binding_order()
    {
        var p = Translate(a => new AuthorHit { Title = a.Name, Subtitle = a.Email, Age = a.Age });
        p.ColumnNames.Should().Equal("name", "email", "age");
    }

    [Fact]
    public void member_init_materializes_hit()
    {
        var p = Translate(a => new AuthorHit { Title = a.Name, Subtitle = a.Email });
        var hit = (AuthorHit)p.Materializer(["Jane", "jane@x.com"]);
        hit.Title.Should().Be("Jane");
        hit.Subtitle.Should().Be("jane@x.com");
    }

    [Fact]
    public void ctor_form_maps_arguments_to_columns()
    {
        var p = Translate(a => new AuthorHit(a.Name, a.Email));
        p.ColumnNames.Should().Equal("name", "email");
        var hit = (AuthorHit)p.Materializer(["Jane", null]);
        hit.Title.Should().Be("Jane");
        hit.Subtitle.Should().BeNull();
    }

    [Fact]
    public void constant_binding_produces_no_column()
    {
        var p = Translate(a => new AuthorHit { Title = a.Name, Badge = "author" });
        p.ColumnNames.Should().Equal("name");
        var hit = (AuthorHit)p.Materializer(["Jane"]);
        hit.Badge.Should().Be("author");
    }

    [Fact]
    public void null_value_materializes_null_property()
    {
        var p = Translate(a => new AuthorHit { Title = a.Name, Subtitle = a.Email });
        var hit = (AuthorHit)p.Materializer(["Jane", null]);
        hit.Subtitle.Should().BeNull();
    }

    [Fact]
    public void method_call_throws()
    {
        FluentActions.Invoking(() => Translate(a => new AuthorHit { Title = a.Name.ToUpper() }))
            .Should().Throw<NotSupportedException>().WithMessage("*ToUpper*");
    }

    [Fact]
    public void nested_member_throws()
    {
        FluentActions.Invoking(() => Translate(a => new AuthorHit { Title = a.Friend!.Name }))
            .Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void assigning_engine_owned_member_throws()
    {
        FluentActions.Invoking(() => Translate(a => new AuthorHit { Title = a.Name, Score = 1.0 }))
            .Should().Throw<NotSupportedException>().WithMessage("*Score*");
    }

    [Fact]
    public void non_new_body_throws()
    {
        var hit = new AuthorHit();
        FluentActions.Invoking(() => Translate(a => hit))
            .Should().Throw<NotSupportedException>();
    }
}
