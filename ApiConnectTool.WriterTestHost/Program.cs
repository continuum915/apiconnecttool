using ApiConnectTool.Services;
using ApiConnectTool.Models;

return ApplicationStartup.Run(args[0], folder =>
{
    var store = new WorkspaceService(folder);
    var workspace = store.LoadWorkspace();
    workspace.Collections.Add(new RequestCollection { Name = args[1] });
    store.SaveWorkspace(workspace);
    Console.WriteLine("ready");
    Console.Out.Flush();
    Console.ReadLine();
}, message => Console.WriteLine(message));
