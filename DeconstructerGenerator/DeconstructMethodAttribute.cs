namespace DeconstructerGenerator;

[System.AttributeUsage(System.AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public class DeconstructMethodAttribute : System.Attribute
{
    /// <summary>
    /// 분해할 매개변수 타입들 (null이면 자동 감지)
    /// </summary>
    public System.Type[]? ParameterTypes { get; set; }
    
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