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
        
        var attrParameterTypes = GetParameterTypesFromAttribute(attribute);
        var attrReturnType = GetReturnTypeFromAttribute(attribute);

        // 디버그 정보 수집
        var debugInfo = new List<string>();
        debugInfo.Add($"Attribute ParameterTypes: {(attrParameterTypes != null ? string.Join(", ", attrParameterTypes.Select(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) : "null")}");
        debugInfo.Add($"Attribute ReturnType: {(attrReturnType != null ? attrReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : "null")}");

        // 1. 매개변수 분해
        var newParameters = new List<string>();

        if (attrParameterTypes != null && attrParameterTypes.Count > 0)
        {
            // ★ Attribute에 ParameterTypes가 있으면 → 그 타입들의 멤버를 직접 분해
            debugInfo.Add("Using Attribute ParameterTypes for decomposition");
            foreach (var paramType in attrParameterTypes)
            {
                var members = GetDeconstructableMembers(paramType);
                debugInfo.Add($"  DecomposeType '{paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}': Members={members.Count} [{string.Join(", ", members.Select(m => $"{m.Type} {m.Name}"))}]");
                foreach (var member in members)
                {
                    newParameters.Add($"{member.Type} {member.Name}");
                }
            }
        }
        else
        {
            // Attribute에 없으면 → 실제 메서드 매개변수를 분석
            debugInfo.Add("No Attribute ParameterTypes, using method parameters");
            for (int i = 0; i < method.Parameters.Length; i++)
            {
                var param = method.Parameters[i];
                bool shouldDeconstruct = ShouldDeconstruct(param.Type);
                debugInfo.Add($"  Param[{i}] '{param.Name}': type={param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, TypeKind={param.Type.TypeKind}, shouldDeconstruct={shouldDeconstruct}");
                
                if (shouldDeconstruct)
                {
                    var members = GetDeconstructableMembers(param.Type);
                    debugInfo.Add($"    -> Members found: {members.Count} [{string.Join(", ", members.Select(m => $"{m.Type} {m.Name}"))}]");
                    foreach (var member in members)
                    {
                        newParameters.Add($"{member.Type} {member.Name}");
                    }
                }
                else
                {
                    newParameters.Add($"{param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {param.Name}");
                }
            }
        }

        // 2. 리턴 타입 처리
        string returnType = "void";

        // ★ Attribute에 ReturnType이 있으면 → 그 타입의 멤버를 Tuple로 분해
        string taskWrapper = "";

        if (method.ReturnsVoid)
        {
            returnType = "void";
        }
        else if (method.ReturnType is INamedTypeSymbol returnTypeSymbol)
        {
            string returnTypeDisplay = returnTypeSymbol.ToDisplayString();
            
            // Task 또는 ValueTask 확인
            bool isTask = returnTypeSymbol.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<TResult>"
                       || returnTypeSymbol.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task";
            bool isValueTask = returnTypeSymbol.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.ValueTask<TResult>"
                            || returnTypeSymbol.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.ValueTask";

            taskWrapper = isTask ? "System.Threading.Tasks.Task" : isValueTask ? "System.Threading.Tasks.ValueTask" : "";

            if (attrReturnType != null)
            {
                // ★ Attribute에서 지정한 ReturnType의 멤버로 분해
                debugInfo.Add($"Using Attribute ReturnType for decomposition: {attrReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}");
                var members = GetDeconstructableMembers(attrReturnType);
                debugInfo.Add($"  ReturnType Members found: {members.Count} [{string.Join(", ", members.Select(m => $"{m.Type} {m.Name}"))}]");

                if (members.Count > 0)
                {
                    var tupleArgs = string.Join(", ", members.Select(m => $"{m.Type} {m.Name}"));
                    if (isTask || isValueTask)
                    {
                        returnType = $"{taskWrapper}<({tupleArgs})>";
                    }
                    else
                    {
                        returnType = $"({tupleArgs})";
                    }
                }
                else
                {
                    returnType = returnTypeDisplay;
                }
            }
            else
            {
                // Attribute에 없으면 → 실제 리턴 타입을 분석
                debugInfo.Add("No Attribute ReturnType, using method return type");

                if (isTask || isValueTask)
                {
                    if (returnTypeSymbol.TypeArguments.Length > 0)
                    {
                        var innerType = returnTypeSymbol.TypeArguments[0];
                        debugInfo.Add($"  ReturnType inner: {innerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, TypeKind={innerType.TypeKind}");

                        if (innerType.SpecialType == SpecialType.System_Void)
                        {
                            returnType = taskWrapper;
                        }
                        else
                        {
                            bool shouldDeconstruct = ShouldDeconstruct(innerType);
                            if (shouldDeconstruct)
                            {
                                var members = GetDeconstructableMembers(innerType);
                                if (members.Count > 0)
                                {
                                    var tupleArgs = string.Join(", ", members.Select(m => $"{m.Type} {m.Name}"));
                                    returnType = $"{taskWrapper}<({tupleArgs})>";
                                }
                                else
                                {
                                    returnType = returnTypeDisplay;
                                }
                            }
                            else
                            {
                                returnType = returnTypeDisplay;
                            }
                        }
                    }
                    else
                    {
                        returnType = taskWrapper;
                    }
                }
                else
                {
                    bool shouldDeconstruct = ShouldDeconstruct(method.ReturnType);
                    if (shouldDeconstruct)
                    {
                        var members = GetDeconstructableMembers(method.ReturnType);
                        if (members.Count > 0)
                        {
                            var tupleArgs = string.Join(", ", members.Select(m => $"{m.Type} {m.Name}"));
                            returnType = $"({tupleArgs})";
                        }
                        else
                        {
                            returnType = returnTypeDisplay;
                        }
                    }
                    else
                    {
                        returnType = returnTypeDisplay;
                    }
                }
            }
        }

        // 3. 코드 생성
        var sourceBuilder = new StringBuilder();
        sourceBuilder.AppendLine("// <auto-generated/>");
        sourceBuilder.AppendLine("#nullable enable");
        
        // 디버그 정보를 주석으로 출력
        sourceBuilder.AppendLine("// === DECONSTRUCT DEBUG INFO ===");
        foreach (var info in debugInfo)
        {
            sourceBuilder.AppendLine($"// {info}");
        }
        sourceBuilder.AppendLine("// === END DEBUG INFO ===");
        
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


    private static List<ITypeSymbol>? GetParameterTypesFromAttribute(AttributeData? attribute)
    {
        if (attribute == null) return null;
        
        // ParameterTypes Named argument 확인
        var paramTypesArg = attribute.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == "ParameterTypes");
        
        if (!paramTypesArg.Equals(default(KeyValuePair<string, TypedConstant>)) 
            && !paramTypesArg.Value.IsNull 
            && paramTypesArg.Value.Kind == TypedConstantKind.Array)
        {
            var types = new List<ITypeSymbol>();
            foreach (var value in paramTypesArg.Value.Values)
            {
                if (value.Value is ITypeSymbol typeSymbol)
                {
                    types.Add(typeSymbol);
                }
            }
            return types.Count > 0 ? types : null;
        }
        
        // Constructor 인자 확인 (params Type[])
        if (attribute.ConstructorArguments.Length > 0)
        {
            var constructorArg = attribute.ConstructorArguments[0];
            if (!constructorArg.IsNull && constructorArg.Kind == TypedConstantKind.Array)
            {
                var types = new List<ITypeSymbol>();
                foreach (var value in constructorArg.Values)
                {
                    if (value.Value is ITypeSymbol typeSymbol)
                    {
                        types.Add(typeSymbol);
                    }
                }
                return types.Count > 0 ? types : null;
            }
        }
        
        return null;
    }

    private static ITypeSymbol? GetReturnTypeFromAttribute(AttributeData? attribute)
    {
        if (attribute == null) return null;
        
        var returnTypeArg = attribute.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == "ReturnType");
        
        if (!returnTypeArg.Equals(default(KeyValuePair<string, TypedConstant>))
            && !returnTypeArg.Value.IsNull 
            && returnTypeArg.Value.Value is ITypeSymbol typeSymbol)
        {
            return typeSymbol;
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
        var currentType = type;
        
        // 상속 계층을 순회하며 모든 public 멤버 수집
        while (currentType != null && currentType.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in currentType.GetMembers())
            {
                if (member.DeclaredAccessibility == Accessibility.Public && 
                    !member.IsStatic)
                {
                    string? memberType = null;
                    
                    if (member is IPropertySymbol prop && !prop.IsIndexer)
                    {
                        memberType = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    }
                    else if (member is IFieldSymbol field)
                    {
                        memberType = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    }
                    
                    if (memberType != null)
                    {
                        // 중복 방지 (자식 클래스에서 override한 경우)
                        if (!members.Any(m => m.Item2 == member.Name))
                        {
                            members.Add((memberType, member.Name));
                        }
                    }
                }
            }
            currentType = currentType.BaseType;
        }
        return members;
    }
}