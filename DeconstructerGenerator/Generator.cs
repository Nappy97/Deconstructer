using System.Text;

namespace DeconstructerGenerator;

public class Generator
{
    public static string Generate(string source)
    {
        var methods = MethodParser.Parse(source);

        StringBuilder builder = new();
        foreach (var method in methods)
        {
            if (method.Name.EndsWith("Async"))
                continue;

            var line = method.ToAsyncText();
            builder.AppendLine(line);
        }

        var lines = builder.ToString();

        return lines;
    }
}