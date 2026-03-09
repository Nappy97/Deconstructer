using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DeconstructerGenerator;

public class MethodParser
{
    public static List<Method> Parse(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var root = syntaxTree.GetRoot() as CompilationUnitSyntax;

        var span = root.Usings.ToString();
        Console.WriteLine(span);

        var methodDeclarations = root!.DescendantNodesAndSelf().OfType<MethodDeclarationSyntax>().ToList();

        if (methodDeclarations.Any())
        {
            return methodDeclarations.Select(x => ParseMethod(x, code)).ToList();
        }
        else
        {
            code = WrapIntoClass(code);
            return Parse(code);
        }
    }

    private static Method ParseMethod(MethodDeclarationSyntax syntax, string code)
    {
        var parameters = syntax.ParameterList.Parameters.Select(
            x => new Parameter(FromTypeSyntax(code, x.Type!), x.Identifier.Text, x.Default)
        );

        var attributes = string.Join("\r\n", syntax.AttributeLists.Select(x => x.ToString()));
        
        Method method = new Method(syntax.Modifiers.ToString(), attributes, syntax.Identifier.Text, FromTypeSyntax(code, syntax.ReturnType), parameters.ToList(), syntax.ToString());

        return method;
    }

    public static string WrapIntoClass(string snippet) => 
        $$"""
          class _{{Guid.NewGuid():N}}
          {
                  {{snippet}}
          }
          """;

    public static string FromTypeSyntax(string code, TypeSyntax syntax)
        => code.Substring(syntax.Span.Start, syntax.Span.End - syntax.Span.Start);
}