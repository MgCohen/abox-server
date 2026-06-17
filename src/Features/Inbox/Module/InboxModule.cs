using System.Reflection;
using ABox.Features.Inbox.List;

namespace ABox.Features.Inbox.Module;

public static class InboxModule
{
    public static Assembly EndpointsAssembly => typeof(ListInboxEndpoint).Assembly;
}
