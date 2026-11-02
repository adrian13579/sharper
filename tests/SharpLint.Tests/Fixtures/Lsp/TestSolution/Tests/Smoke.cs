namespace Tests;

public static class Smoke
{
    public static int Check() => Application.Consumer.Read(new Domain.CustomTarget());
}
