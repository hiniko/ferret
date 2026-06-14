using System.Data.Common;
using Npgsql;

namespace Ferret.Core.Sql;

internal static class DbCommandExtensions
{
    public static void AddParameter(this DbCommand command, string name, object? value)
    {
        if (command is NpgsqlCommand npg && value is Array array && value is not byte[])
        {
            npg.Parameters.Add(BuildArrayParameter(name, array));
            return;
        }

        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        command.Parameters.Add(p);
    }

    private static NpgsqlParameter BuildArrayParameter(string name, Array array) => array switch
    {
        Guid[] a    => new NpgsqlParameter<Guid[]>(name, a),
        int[] a     => new NpgsqlParameter<int[]>(name, a),
        long[] a    => new NpgsqlParameter<long[]>(name, a),
        short[] a   => new NpgsqlParameter<short[]>(name, a),
        decimal[] a => new NpgsqlParameter<decimal[]>(name, a),
        double[] a  => new NpgsqlParameter<double[]>(name, a),
        float[] a   => new NpgsqlParameter<float[]>(name, a),
        bool[] a    => new NpgsqlParameter<bool[]>(name, a),
        string[] a  => new NpgsqlParameter<string[]>(name, a),
        DateTime[] a => new NpgsqlParameter<DateTime[]>(name, a),
        _ => new NpgsqlParameter(name, array),
    };
}
