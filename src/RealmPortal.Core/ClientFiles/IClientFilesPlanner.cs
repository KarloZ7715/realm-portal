namespace RealmPortal.Core.ClientFiles;

/// <summary>Computes, without writing anything, which client files change for a request and their exact new bytes.</summary>
public interface IClientFilesPlanner
{
    /// <inheritdoc cref="IClientFilesApplier.Plan"/>
    ClientFilesPlan Plan(ClientFilesRequest request);
}
