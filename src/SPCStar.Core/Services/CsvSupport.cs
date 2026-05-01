using System.Text;

namespace SPCStar.Core.Services;

public static class CsvSupport
{
    public static IReadOnlyList<Dictionary<string, string>> ReadRows(string csv)
    {
        var lines = csv.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return [];
        }

        var headers = ParseLine(lines[0]).Select(header => header.Trim()).ToArray();
        var rows = new List<Dictionary<string, string>>();

        foreach (var line in lines.Skip(1))
        {
            var values = ParseLine(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
            {
                row[headers[i]] = i < values.Count ? values[i].Trim() : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    public static string WriteRows(IEnumerable<string> headers, IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        var headerList = headers.ToArray();
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", headerList.Select(Escape)));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", headerList.Select(header => Escape(row.GetValueOrDefault(header) ?? string.Empty))));
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> ParseLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && inQuotes && i + 1 < line.Length && line[i + 1] == '"')
            {
                current.Append('"');
                i++;
            }
            else if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        values.Add(current.ToString());
        return values;
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
