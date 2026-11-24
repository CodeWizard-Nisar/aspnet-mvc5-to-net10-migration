namespace ConfReg.Domain.Abstractions
{
    /// <summary>
    /// Turns an application-relative path ("~/App_Data/x.log") into a physical one. Replaces Server.MapPath.
    /// A worker or test resolves against a directory it chooses (DirectoryAppPaths).
    /// </summary>
    public interface IAppPaths
    {
        string Resolve(string appRelativePath);
    }
}
