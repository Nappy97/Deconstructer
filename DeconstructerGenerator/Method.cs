using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DeconstructerGenerator;

public class Method(string modifier, string attribute, string name, string returnType, List<Parameter> parameters, string code)
{
    public string AsyncReturnType => ReturnType switch
    {
        "void" => "Task",
        _ => $"Task<{ReturnType}>"
    };

    public string Modifier { get; } = modifier;
    public string Attribute { get; } = attribute;
    public string Name { get; } = name;
    public string ReturnType { get; } = returnType;
    public List<Parameter> Parameters { get; } = parameters;
    public string Code { get; } = code;

    public override string ToString()
    {
        StringBuilder builder = new ();
        builder.AppendLine($"{AsyncReturnType} {Name}");
        foreach (var parameter in Parameters)
            builder.AppendLine($"\t{parameter}");

        return builder.ToString();
    }
    
    public string ToAsyncText()
    {
        // public Task SleepAsync(int seconds, int milliSeconds) => Task.Run(() => Sleep(seconds, milliSeconds));
        // public Task<List<int>> GetEvensAsync()  => Task.Run(() => GetEvens());

        string typeAndNames = string.Join(", ", Parameters);
        string names = string.Join(", ", Parameters.Select(x => x.Name));

        return $"""
                {Modifier} {AsyncReturnType} {Name}Async({typeAndNames}) => Task.Run(() => {Name}({names}));
                """;
    }
}

public class Parameter(string type, string name, EqualsValueClauseSyntax argDefault)
{
    public string Type { get; } = type;
    public string Name { get; } = name;
    public EqualsValueClauseSyntax ArgDefault { get; } = argDefault;

    public override string ToString()
    {
        return ArgDefault switch
        {
            null => $"{Type} {Name}",
            _ => $"{Type} {Name} = {ArgDefault.Value}",
        };
    }
}