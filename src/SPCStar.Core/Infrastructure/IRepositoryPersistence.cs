namespace SPCStar.Core.Infrastructure;

public interface IRepositoryPersistence
{
    string StoragePath { get; }
    void SaveChanges();
}
