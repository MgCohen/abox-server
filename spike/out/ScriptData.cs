public static class ScriptData
{
    public static int Run()
    {
        int acc = 0;
        for (int i = 0; i < 5; i++)
        {
            acc = acc + i;
        }

        return acc;
    }
}