namespace WireMarkerInspection.Desktop.Security;

public enum AccessLevel
{
    Operator,
    Admin
}

public interface IAuthenticationService
{
    bool Authenticate(string username,string password);
}

public sealed class LocalAuthenticationService : IAuthenticationService
{
    public bool Authenticate(string username,string password)=>
        string.Equals(username,"admin",StringComparison.Ordinal)&&
        string.Equals(password,"admin",StringComparison.Ordinal);
}
