namespace SPCStar.Core.Infrastructure;

public interface IRepositoryPersistence
{
    string StoragePath { get; }
    void SaveChanges();
    void BackupTo(string backupPath);
    void RestoreFrom(string backupPath);
}
