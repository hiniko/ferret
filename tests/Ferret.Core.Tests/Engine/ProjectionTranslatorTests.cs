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
        public Author() { }

        public Author(string name, string? email)
        {
            Name = name;
            Email = email;
        }

        public Guid Id { get; set; }
        [Searchable] public string Name { get; set; } = "";
        public string? Email { get; set; }
        public int Age { get; set; }
        public string Badge { get; set; } = "";
        public Author? Friend { get; set; }
    }

    private sealed class NotTheEntity
    {
    }

    private static EntityModel Model() => EntityRegistry
        .Build([typeof(Author)], new SnakeCaseNamingStrategy())
        .Get<Author>();

    private static CompiledProjection Translate(Expression<Func<Author, Author>> projection) =>
        ProjectionTranslator.Translate(projection, Model());

    [Fact]
    public void member_init_maps_properties_to_columns_in_binding_order()
    {
        var p = Translate(a => new Author { Name = a.Name, Email = a.Email, Age = a.Age });
        p.ColumnNames.Should().Equal("name", "email", "age");
    }

    [Fact]
    public void member_init_materializes_partial_entity()
    {
        var p = Translate(a => new Author { Name = a.Name, Email = a.Email });
        var hit = (Author)p.Materializer(["Jane", "jane@x.com"]);
        hit.Name.Should().Be("Jane");
        hit.Email.Should().Be("jane@x.com");
        hit.Age.Should().Be(0);
    }

    [Fact]
    public void ctor_form_maps_arguments_to_columns()
    {
        var p = Translate(a => new Author(a.Name, a.Email));
        p.ColumnNames.Should().Equal("name", "email");
        var hit = (Author)p.Materializer(["Jane", null]);
        hit.Name.Should().Be("Jane");
        hit.Email.Should().BeNull();
    }

    [Fact]
    public void constant_binding_produces_no_column()
    {
        var p = Translate(a => new Author { Name = a.Name, Badge = "author" });
        p.ColumnNames.Should().Equal("name");
        var hit = (Author)p.Materializer(["Jane"]);
        hit.Badge.Should().Be("author");
    }

    [Fact]
    public void null_value_materializes_null_property()
    {
        var p = Translate(a => new Author { Name = a.Name, Email = a.Email });
        var hit = (Author)p.Materializer(["Jane", null]);
        hit.Email.Should().BeNull();
    }

    [Fact]
    public void method_call_throws()
    {
        FluentActions.Invoking(() => Translate(a => new Author { Name = a.Name.ToUpper() }))
            .Should().Throw<NotSupportedException>().WithMessage("*ToUpper*");
    }

    [Fact]
    public void nested_member_throws()
    {
        FluentActions.Invoking(() => Translate(a => new Author { Name = a.Friend!.Name }))
            .Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void constructing_a_different_type_throws()
    {
        LambdaExpression projection = (Expression<Func<Author, NotTheEntity>>)(a => new NotTheEntity());
        FluentActions.Invoking(() => ProjectionTranslator.Translate(projection, Model()))
            .Should().Throw<NotSupportedException>().WithMessage("*must construct Author*");
    }

    [Fact]
    public void non_new_body_throws()
    {
        var existing = new Author();
        FluentActions.Invoking(() => Translate(a => existing))
            .Should().Throw<NotSupportedException>();
    }
}
