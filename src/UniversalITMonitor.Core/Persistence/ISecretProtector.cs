namespace UniversalITMonitor.Core.Persistence;

public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}
