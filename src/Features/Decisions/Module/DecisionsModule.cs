using System.Reflection;
using ABox.Features.Decisions.List;

namespace ABox.Features.Decisions.Module;

public static class DecisionsModule
{
    public static Assembly EndpointsAssembly => typeof(ListDecisionsEndpoint).Assembly;
}
