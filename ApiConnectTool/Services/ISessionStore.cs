using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public interface ISessionStore
{
    SessionState Load();
    void Save(SessionState state);
}
