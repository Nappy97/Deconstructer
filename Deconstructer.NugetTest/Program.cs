using DeconstructerGenerator;

namespace Deconstructer.NugetTest;

public class UserInfo
{
    public string Name { get; set; }
    public int Age { get; set; }
}

public partial class Program
{
    static async Task Main(string[] args)
    {
    }



    // Class 매개변수 Unfold 테스트 (body 없음 + class 매개변수 → unfold 생성)
    [DeconstructMethod]
    public string CreateUser(UserInfo user)
    {
        return $"{user.Name} {user.Age}";
    }
}