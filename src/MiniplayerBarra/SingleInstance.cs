namespace MiniplayerBarra;

/// <summary>Garante uma só instância por sessão do Windows (mutex nomeado).</summary>
public static class SingleInstance
{
    /// <summary>Devolve o "token" da instância, ou null se outra já estiver rodando.</summary>
    public static IDisposable? TryAcquire(string name)
    {
        var mutex = new Mutex(false, @"Local\" + name, out bool createdNew);
        if (createdNew) return mutex;
        mutex.Dispose();
        return null;
    }
}
