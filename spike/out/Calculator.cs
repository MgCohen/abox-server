public class Calculator
{
    public System.Int32 Run()
    {
        int acc = 0;
        for (int i = 0; i < 5; i++)
        {
            acc = acc + i;
        }

        return acc;
    }
}
