namespace DeconstructerGenerator;

[System.AttributeUsage(System.AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public class DeconstructMethodAttribute : System.Attribute
{
    /// <summary>
    /// 분해할 매개변수 타입들 (null이면 자동 감지)
    /// </summary>
    public System.Type[]? ParameterTypes { get; set; }
    
    /// <summary>
    /// ParameterTypes와 1:1 매칭되는 매개변수 이름들
    /// 클래스 타입은 멤버명이 사용되므로 빈 문자열("") 또는 null 가능
    /// 프리미티브 타입(string, int 등)에 이름을 지정할 때 사용
    /// </summary>
    public string[]? ParameterNames { get; set; }
    
    /// <summary>
    /// 분해할 리턴 타입 (null이면 자동 감지)
    /// </summary>
    public System.Type? ReturnType { get; set; }
    
    public DeconstructMethodAttribute()
    {
    }
    
    public DeconstructMethodAttribute(params System.Type[] parameterTypes)
    {
        ParameterTypes = parameterTypes;
    }
}