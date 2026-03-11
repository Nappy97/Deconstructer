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
        var attrParameterNames = GetParameterNamesFromAttribute(attribute);
        var attrReturnType = GetReturnTypeFromAttribute(attribute);

        // 디버그 정보 수집
        var debugInfo = new List<string>();
        debugInfo.Add($"Attribute ParameterTypes: {(attrParameterTypes != null ? string.Join(", ", attrParameterTypes.Select(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) : "null")}");
        debugInfo.Add($"Attribute ReturnType: {(attrReturnType != null ? attrReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : "null")}");

        // 1. 매개변수 분해
        var newParameters = new List<string>();

        if (attrParameterTypes != null && attrParameterTypes.Count > 0)
        {
            // ★ Attribute에 ParameterTypes가 있으면 → 그 타입들의 멤버를 직접 분해 (프리미티브는 제외)
            debugInfo.Add("Using Attribute ParameterTypes for decomposition");
            for (int i = 0; i < attrParameterTypes.Count; i++)
            {
                var paramType = attrParameterTypes[i];
                // ParameterNames에서 해당 인덱스의 이름 가져오기
                string paramName = (attrParameterNames != null && i < attrParameterNames.Count) 
                    ? attrParameterNames[i] 
                    : "";

                if (ShouldDeconstruct(paramType))
                {
                    var members = GetDeconstructableMembers(paramType);
                    debugInfo.Add($"  DecomposeType '{paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}': Members={members.Count} [{string.Join(", ", members.Select(m => $"{m.Type} {m.Name}"))}]");
                    foreach (var member in members)
                    {
                        newParameters.Add($"{member.Type} {ToCamelCase(member.Name)}");
                    }
                }
                else
                {
                    // 프리미티브 타입: ParameterNames에서 이름 사용, 없으면 타입명 기반
                    string name = !string.IsNullOrEmpty(paramName) 
                        ? paramName 
                        : ToCamelCase(paramType.Name);
                    debugInfo.Add($"  PrimitiveType '{paramType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}': name='{name}'");
                    newParameters.Add($"{paramType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {name}");
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
                        newParameters.Add($"{member.Type} {ToCamelCase(member.Name)}");
                    }
                }
                else
                {
                    newParameters.Add($"{param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {param.Name}");
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

            taskWrapper = isTask ? "Task" : isValueTask ? "ValueTask" : "";

            if (attrReturnType != null)
            {
                // ★ Attribute에서 지정한 ReturnType 처리
                debugInfo.Add($"Using Attribute ReturnType: {attrReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}");
                
                if (ShouldDeconstruct(attrReturnType))
                {
                    // 클래스 타입 → 멤버로 분해하여 Tuple
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
                    // 프리미티브 타입 → 그대로 리턴
                    debugInfo.Add($"  ReturnType is primitive, keep as-is: {attrReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}");
                    string attrReturnTypeName = attrReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    if (isTask || isValueTask)
                    {
                        returnType = $"{taskWrapper}<{attrReturnTypeName}>";
                    }
                    else
                    {
                        returnType = attrReturnTypeName;
                    }
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

    private static List<string>? GetParameterNamesFromAttribute(AttributeData? attribute)
    {
        if (attribute == null) return null;
        
        var paramNamesArg = attribute.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == "ParameterNames");
        
        if (!paramNamesArg.Equals(default(KeyValuePair<string, TypedConstant>))
            && !paramNamesArg.Value.IsNull 
            && paramNamesArg.Value.Kind == TypedConstantKind.Array)
        {
            var names = new List<string>();
            foreach (var value in paramNamesArg.Value.Values)
            {
                names.Add(value.Value as string ?? "");
            }
            return names.Count > 0 ? names : null;
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
                        memberType = prop.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    }
                    else if (member is IFieldSymbol field)
                    {
                        memberType = field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
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

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0])) return name;
        if (name.Length == 1) return name.ToLowerInvariant();
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}