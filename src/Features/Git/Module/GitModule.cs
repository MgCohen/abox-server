using System.Reflection;
using ABox.Features.Git.PrList;

namespace ABox.Features.Git.Module;

public static class GitModule
{
    public static Assembly EndpointsAssembly => typeof(PrListEndpoint).Assembly;
}
