using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DeconstructerGenerator;

// DeconstructGenerator.cs
[Generator]
public class DeconstructGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methodProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsMethodWithAttribute(s),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(methodProvider,
            static (spc, source) => Execute(source!, spc));
    }

    private static bool IsMethodWithAttribute(SyntaxNode node)
    {
        return node is MethodDeclarationSyntax method &&
               method.AttributeLists.Count > 0;
    }

    private static IMethodSymbol? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol?.GetAttributes().Any(ad => 
            ad.AttributeClass?.ToDisplayString() == "DeconstructerGenerator.DeconstructMethodAttribute") == true)
        {
            return methodSymbol;
        }

        return null;
    }

private static void Execute(IMethodSymbol method, SourceProductionContext context)
{
    if (method.ContainingType is not INamedTypeSymbol classSymbol) return;

    string namespaceName = method.ContainingNamespace.IsGlobalNamespace 
        ? "" 
        : method.ContainingNamespace.ToDisplayString();
    
    string className = classSymbol.Name;
    string methodName = method.Name;
    string generatedMethodName = $"{methodName}_Deconstructed";

    // Attribute에서 타입 정보 가져오기
    var attribute = method.GetAttributes()
        .FirstOrDefault(ad => ad.AttributeClass?.ToDisplayString() == "DeconstructerGenerator.DeconstructMethodAttribute");
    
    var parameterTypesToDeconstruct = GetParameterTypesFromAttribute(attribute);
    var returnTypeToDeconstruct = GetReturnTypeFromAttribute(attribute);

    // 1. 매개변수 분해
    var newParameters = new List<string>();
    for (int i = 0; i < method.Parameters.Length; i++)
    {
        var param = method.Parameters[i];
        
        // Attribute에서 지정한 타입이거나, 자동 감지
        bool shouldDeconstruct = parameterTypesToDeconstruct != null
            ? parameterTypesToDeconstruct.Contains(param.Type.ToDisplayString())
            : ShouldDeconstruct(param.Type);
        
        if (shouldDeconstruct)
        {
            var members = GetDeconstructableMembers(param.Type);
            foreach (var member in members)
            {
                newParameters.Add($"{member.Type} {member.Name}");
            }
        }
        else
        {
            newParameters.Add($"{param.Type} {param.Name}");
        }
    }

    // 2. 리턴 타입 처리
    string returnType = "void";

    if (method.ReturnsVoid)
    {
        returnType = "void";
    }
    else if (method.ReturnType is INamedTypeSymbol returnTypeSymbol)
    {
        string returnTypeDisplay = returnTypeSymbol.ToDisplayString();
        
        // Task 또는 ValueTask 확인
        if (returnTypeDisplay.StartsWith("System.Threading.Tasks.Task") || 
            returnTypeDisplay.StartsWith("System.Threading.Tasks.ValueTask"))
        {
            // Task<T> 의 T 추출
            if (returnTypeSymbol.TypeArguments.Length > 0)
            {
                var innerType = returnTypeSymbol.TypeArguments[0];
                
                if (innerType.SpecialType == SpecialType.System_Void)
                {
                    returnType = "Task";
                }
                else
                {
                    // Attribute에서 지정한 타입이거나, 자동 감지
                    bool shouldDeconstruct = returnTypeToDeconstruct != null
                        ? returnTypeToDeconstruct == innerType.ToDisplayString()
                        : ShouldDeconstruct(innerType);
                    
                    if (shouldDeconstruct)
                    {
                        var members = GetDeconstructableMembers(innerType);
                        var tupleArgs = string.Join(", ", members.Select(m => $"{m.Type} {m.Name}"));
                        returnType = $"Task<({tupleArgs})>";
                    }
                    else
                    {
                        returnType = returnTypeDisplay;
                    }
                }
            }
            else
            {
                returnType = "Task";
            }
        }
        else
        {
            // Task 가 아닌 일반 반환 타입
            bool shouldDeconstruct = returnTypeToDeconstruct != null
                ? returnTypeToDeconstruct == method.ReturnType.ToDisplayString()
                : ShouldDeconstruct(method.ReturnType);
            
            if (shouldDeconstruct)
            {
                var members = GetDeconstructableMembers(method.ReturnType);
                var tupleArgs = string.Join(", ", members.Select(m => $"{m.Type} {m.Name}"));
                returnType = $"({tupleArgs})";
            }
            else
            {
                returnType = returnTypeDisplay;
            }
        }
    }

    // 3. 코드 생성
    var sourceBuilder = new StringBuilder();
    sourceBuilder.AppendLine("// <auto-generated/>");
    sourceBuilder.AppendLine("#nullable enable");
    
    if (!string.IsNullOrEmpty(namespaceName))
    {
        sourceBuilder.AppendLine($"namespace {namespaceName}");
        sourceBuilder.AppendLine("{");
    }

    sourceBuilder.AppendLine($"    partial class {className}");
    sourceBuilder.AppendLine("    {");
    
    sourceBuilder.AppendLine($"        private partial {returnType} {generatedMethodName}({string.Join(", ", newParameters)});");
    
    sourceBuilder.AppendLine("    }");

    if (!string.IsNullOrEmpty(namespaceName))
    {
        sourceBuilder.AppendLine("}");
    }

    context.AddSource($"{className}_{generatedMethodName}.g.cs", sourceBuilder.ToString());
}


    private static HashSet<string>? GetParameterTypesFromAttribute(AttributeData? attribute)
    {
        if (attribute == null) return null;
        
        // ParameterTypes 프로퍼티 찾기
        var paramTypesArg = attribute.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == "ParameterTypes");
        
        if (paramTypesArg.Value.IsNull) 
        {
            // Constructor 인자 확인
            if (attribute.ConstructorArguments.Length > 0)
            {
                var constructorArg = attribute.ConstructorArguments[0];
                if (!constructorArg.IsNull && constructorArg.Kind == TypedConstantKind.Array)
                {
                    var types = new HashSet<string>();
                    foreach (var value in constructorArg.Values)
                    {
                        if (value.Value is INamedTypeSymbol typeSymbol)
                        {
                            types.Add(typeSymbol.ToDisplayString());
                        }
                    }
                    return types.Count > 0 ? types : null;
                }
            }
            return null;
        }
        
        if (paramTypesArg.Value.Kind == TypedConstantKind.Array)
        {
            var types = new HashSet<string>();
            foreach (var value in paramTypesArg.Value.Values)
            {
                if (value.Value is INamedTypeSymbol typeSymbol)
                {
                    types.Add(typeSymbol.ToDisplayString());
                }
            }
            return types.Count > 0 ? types : null;
        }
        
        return null;
    }

    private static string? GetReturnTypeFromAttribute(AttributeData? attribute)
    {
        if (attribute == null) return null;
        
        var returnTypeArg = attribute.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == "ReturnType");
        
        if (!returnTypeArg.Value.IsNull && returnTypeArg.Value.Value is INamedTypeSymbol typeSymbol)
        {
            return typeSymbol.ToDisplayString();
        }
        
        return null;
    }

    private static bool ShouldDeconstruct(ITypeSymbol type)
    {
        // Primitive 타입, enum 등 제외
        if (type.SpecialType != SpecialType.None) return false;
        
        // Struct 제외
        if (type.TypeKind == TypeKind.Struct) return false;
        
        // Class 타입만 허용
        if (type.TypeKind != TypeKind.Class) return false;
        
        // Task, ValueTask 제외
        string typeName = type.ToDisplayString();
        if (typeName.StartsWith("System.Threading.Tasks.")) return false;
        
        // System.String 등 기본 시스템 타입 제외
        if (typeName == "string" || typeName == "System.String") return false;
        if (typeName == "object" || typeName == "System.Object") return false;
        
        return true;
    }

    private static List<(string Type, string Name)> GetDeconstructableMembers(ITypeSymbol type)
    {
        var members = new List<(string, string)>();
        foreach (var member in type.GetMembers())
        {
            if (member.DeclaredAccessibility == Accessibility.Public && 
                !member.IsStatic && 
                member is (IPropertySymbol { IsIndexer: false } or IFieldSymbol))
            {
                string memberType = member switch
                {
                    IPropertySymbol prop => prop.Type.ToDisplayString(),
                    IFieldSymbol field => field.Type.ToDisplayString(),
                };
                members.Add((memberType, member.Name));
            }
        }
        return members;
    }
}