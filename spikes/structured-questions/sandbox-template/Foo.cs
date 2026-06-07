namespace SampleService;

public static class Foo
{
    public static int Sum(int[] values)
    {
        var tmp = 0;
        foreach (var v in values)
        {
            tmp += v;
        }

        return tmp;
    }
}
