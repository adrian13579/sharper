namespace Application;

public static class Consumer
{
    public static int Read(Domain.CustomTarget value) => value.Value;
}
