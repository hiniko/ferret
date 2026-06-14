using Ferret.Abstractions;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine.Cursor;

public class CursorFingerprintTests
{
    [Fact]
    public void Same_inputs_produce_same_fingerprint()
    {
        var a = CursorFingerprint.Compute("products",
            sort: [new SortClause { Field = "Name", Direction = SortDirection.Ascending }],
            filter: [new FilterClause { Field = "Category", Operator = FilterOperator.Equals, Value = "tools" }],
            keyColumns: ["Id"]);
        var b = CursorFingerprint.Compute("products",
            sort: [new SortClause { Field = "Name", Direction = SortDirection.Ascending }],
            filter: [new FilterClause { Field = "Category", Operator = FilterOperator.Equals, Value = "tools" }],
            keyColumns: ["Id"]);
        a.Should().Be(b);
    }

    [Fact]
    public void Different_table_changes_fingerprint()
    {
        var a = CursorFingerprint.Compute("products", sort: [], filter: [], keyColumns: ["Id"]);
        var b = CursorFingerprint.Compute("orders", sort: [], filter: [], keyColumns: ["Id"]);
        a.Should().NotBe(b);
    }

    [Fact]
    public void Different_sort_changes_fingerprint()
    {
        var a = CursorFingerprint.Compute("products",
            sort: [new SortClause { Field = "Name", Direction = SortDirection.Ascending }],
            filter: [],
            keyColumns: ["Id"]);
        var b = CursorFingerprint.Compute("products",
            sort: [new SortClause { Field = "Name", Direction = SortDirection.Descending }],
            filter: [],
            keyColumns: ["Id"]);
        a.Should().NotBe(b);
    }

    [Fact]
    public void Filter_value_change_does_not_affect_fingerprint()
    {
        var a = CursorFingerprint.Compute("products", sort: [],
            filter: [new FilterClause { Field = "Price", Operator = FilterOperator.LessThan, Value = "50" }],
            keyColumns: ["Id"]);
        var b = CursorFingerprint.Compute("products", sort: [],
            filter: [new FilterClause { Field = "Price", Operator = FilterOperator.LessThan, Value = "60" }],
            keyColumns: ["Id"]);
        a.Should().Be(b);
    }

    [Fact]
    public void Fingerprint_is_16_lowercase_hex_chars()
    {
        var fp = CursorFingerprint.Compute("products", sort: [], filter: [], keyColumns: ["Id"]);
        fp.Should().MatchRegex("^[0-9a-f]{16}$");
    }

    [Fact]
    public void Differs_when_key_columns_change()
    {
        var a = CursorFingerprint.Compute("orders", sort: [], filter: [],
            keyColumns: ["OrderId"]);
        var b = CursorFingerprint.Compute("orders", sort: [], filter: [],
            keyColumns: ["OrderId", "LineId"]);
        a.Should().NotBe(b);
    }

    [Fact]
    public void Stable_for_same_key_shape()
    {
        var a = CursorFingerprint.Compute("orders", sort: [], filter: [],
            keyColumns: ["OrderId", "LineId"]);
        var b = CursorFingerprint.Compute("orders", sort: [], filter: [],
            keyColumns: ["OrderId", "LineId"]);
        a.Should().Be(b);
    }
}
